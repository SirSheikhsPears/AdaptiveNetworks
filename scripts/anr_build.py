"""ANR local version reservation, packaging and protected deployment.
Python 3.11+; Git; integrated with MSBuild.
Does not invoke the C# compiler directly.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid
import zipfile
from anr_common import *

def utc(): return datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
def dump(p, obj, exclusive=False):
    p=Path(p); p.parent.mkdir(parents=True,exist_ok=True)
    with p.open('x' if exclusive else 'w',encoding='utf-8',newline='\n') as f:
        json.dump(obj,f,indent=2,ensure_ascii=False); f.write('\n'); f.flush(); os.fsync(f.fileno())
def read(p): return json.loads(Path(p).read_text(encoding='utf-8'))
def ci(): return any(os.environ.get(k,'').lower() not in ('','0','false') for k in ('GITHUB_ACTIONS','CI','TF_BUILD'))
def local_only(): require(not ci(),'Local ledger/deploy/archive operations are blocked on CI')
def state(repo): return repo/'.anr-build-state'
def git(repo,*args):
    return subprocess.check_output(['git','-C',str(repo),*args],stderr=subprocess.PIPE)
def snapshot(repo):
    kc=repo/'AdaptiveRoads/KianCommons'
    head=git(repo,'rev-parse','HEAD').decode().strip()
    khead=git(kc,'rev-parse','HEAD').decode().strip()
    require(COMMIT.fullmatch(head) and COMMIT.fullmatch(khead),'Missing Git source identity')
    dirty=bool(git(repo,'status','--porcelain=v1','--untracked-files=all').strip() or
               git(kc,'status','--porcelain=v1','--untracked-files=all').strip())
    records=[]
    for root,prefix in ((repo,''),(kc,'AdaptiveRoads/KianCommons/')):
        for name in sorted(set(git(root,'ls-files','-z','--cached','--others','--exclude-standard').split(b'\0'))):
            if not name: continue
            rel=os.fsdecode(name); p=root/rel
            if p.is_dir(): continue
            # Missing tracked source is part of the fingerprint, not silently ignored.
            records.append([prefix+rel,sha(p.read_bytes()) if p.is_file() else 'MISSING'])
    return {'source_commit':head,'kiancommons_commit':khead,'source_state':'dirty' if dirty else 'clean',
            'source_fingerprint':sha(json.dumps(records,sort_keys=True).encode())}
def locked(repo,token):
    require(read(state(repo)/'active.lock')['token']==token,'Wrong build lock owner')
def lock(repo,token):
    local_only(); require((state(repo)/'initialized.json').is_file(),'Initialize ledger explicitly before Build')
    baseline=read(state(repo)/'initialized.json')['baseline']
    require((state(repo)/'reservations'/baseline.rsplit('.',1)[0]/(baseline+'.json')).is_file(),'Baseline reservation missing: ledger recovery required')
    require(re.fullmatch(r'[a-f0-9]{32}',token),'Invalid run token')
    dump(state(repo)/'active.lock',{'token':token,'started_utc':utc()},True)
def unlock(repo,token):
    local_only()
    p=state(repo)/'active.lock'
    if not p.exists(): return
    locked(repo,token)
    p.unlink()
def safe_root(path):
    p=Path(path).absolute()
    require(str(p)==str(p.resolve()),'Symlink/reparse path not accepted for mutation')
    for part in [p,*p.parents]:
        if part.exists():
            require(not part.is_symlink() and not (getattr(part.stat(),'st_file_attributes',0)&1024),
                    'Reparse point is not accepted for mutation')
    return p
def default_mods():
    if os.name=='nt': return Path(os.environ['LOCALAPPDATA'])/'Colossal Order/Cities_Skylines/Addons/Mods'
    return Path.home()/'Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods'
def check_release(package):
    receipt=read(package/'VERIFICATION/build.json')
    name=package_name(receipt['channel'],receipt['version'])
    data=(package/'GITHUB'/name).read_bytes(); side=(package/'GITHUB'/name.replace('.zip','.sha256')).read_bytes()
    m=validate_zip(data,side,sha(data),receipt['channel'],receipt['version'],receipt['source_commit'],public=False)
    for item in m['payload']:
        require(sha((package/'Content'/item['file']).read_bytes())==item['sha256'],'Archive Content differs from ZIP')
    with zipfile.ZipFile(package/'GITHUB'/name) as z:
        require(z.read('BUILD_INFO.md')==(package/'Content/BUILD_INFO.md').read_bytes(),'Archive manifest differs')
    require({p.name for p in (package/'Content').iterdir()}=={p['file'] for p in m['payload']}|{'BUILD_INFO.md'},'Unexpected Content files')
    if receipt.get('preview_sha256'):
        require(sha((package/'PreviewImage.png').read_bytes())==receipt['preview_sha256'],'Archive preview differs')
    return receipt,name
def archive(package, destination):
    local_only(); receipt,name=check_release(package)
    base=safe_root(destination)
    if os.name=='nt':
        exact=Path('R:/ADVANCED Networks/REMAIN/GITHUB-STEAM')/('ALPHA' if receipt['channel']=='Alpha' else 'STABLE')
        require(base==exact.absolute(),'Windows archive root must be the owner-approved R: channel path')
    require(base.is_dir(),'Archive root must already exist; verify R: or mounted location')
    target=base/receipt['version']
    if target.exists():
        old,oldname=check_release(target)
        require((target/'GITHUB'/oldname).read_bytes()==(package/'GITHUB'/name).read_bytes(),
                'Existing archive has different bytes; never overwrite')
        return
    temp=base/('.ANR-pending-'+uuid.uuid4().hex)
    shutil.copytree(package,temp)
    check_release(temp)
    # Claim final directory atomically. A crash leaves an incomplete directory; never overwrite it automatically.
    target.mkdir(exist_ok=False)
    try:
        for p in temp.iterdir(): shutil.move(str(p),str(target/p.name))
        check_release(target)
        temp.rmdir()
    except Exception:
        raise ValueError('Archive incomplete; preserve pending and version directories for manual recovery')
def deploy_and_zip(package, deploy, enabled_deploy=True, enabled_zip=True):
    local_only()
    mods=safe_root(deploy).parent
    require(mods.is_dir(),'Deploy parent must exist')
    rootlock=mods/'.anr-deploy.lock'
    token=uuid.uuid4().hex
    dump(rootlock,{'token':token,'utc':utc()},True)
    try: _deploy_and_zip(package,deploy,enabled_deploy,enabled_zip)
    finally:
        require(read(rootlock)['token']==token,'Deploy lock changed')
        rootlock.unlink()

def _deploy_and_zip(package, deploy, enabled_deploy=True, enabled_zip=True):
    receipt=read(package/'VERIFICATION/build.json'); channel=receipt['channel']
    expected='Adaptive Networks Remain Alpha' if channel=='Alpha' else 'Adaptive Networks Remain'
    deploy=safe_root(deploy)
    require(deploy.name==expected,'DeployDir leaf must identify the selected ANR channel')
    mods=deploy.parent
    require(mods.is_dir(),'Deploy parent must exist and be reviewed')
    name=package_name(channel,receipt['version']); source=package/'GITHUB'/name
    digest=sha(source.read_bytes())
    # Prepare a validated ZIP beside the destination before touching the previous installation.
    tempzip=mods/('.ANR-pending-'+uuid.uuid4().hex+'.zip')
    if enabled_zip:
        with tempzip.open('xb') as f: f.write(source.read_bytes()); f.flush(); os.fsync(f.fileno())
        require(sha(tempzip.read_bytes())==digest,'Local ZIP copy mismatch')
        final=mods/name
        if final.exists(): require(sha(final.read_bytes())==digest,'Version collision in local ZIP')
    if enabled_deploy:
        work=mods.parent/'.ANR-work'; work.mkdir(exist_ok=True); safe_root(work)
        stage=work/('pending-'+uuid.uuid4().hex)
        shutil.copytree(package/'Content',stage)
        require(all(sha(p.read_bytes())==sha((stage/p.name).read_bytes()) for p in (package/'Content').iterdir()),'Staged deploy copy mismatch')
        # The private marker does not enter public Content or ZIP.
        dump(stage/'.anr-owned.json',{'channel':channel,'zip_sha256':digest})
        backup=None
        if deploy.exists():
            marker=deploy/'.anr-owned.json'
            require(marker.is_file() and read(marker)['channel']==channel,
                    'Existing unowned mod directory: owner must back it up and move it first')
            backup=work/('previous-'+uuid.uuid4().hex)
            deploy.rename(backup)
        try: stage.rename(deploy)
        except Exception:
            if backup: backup.rename(deploy)
            raise
        # Keep previous folder for recovery; do not delete user files.
    if enabled_zip:
        final=mods/name
        if final.exists(): tempzip.unlink()
        else:
            os.link(tempzip,final)  # create-only: never overwrite a concurrently created ZIP
            tempzip.unlink()
        require(sha(final.read_bytes())==digest,'Final local ZIP mismatch')
        # Only remove old ZIPs in the actual default Addons/Mods root, and only if recorded by this script.
        history=mods/('.anr-alpha-zip.json' if channel=='Alpha' else '.anr-stable-zip.json')
        previous=read(history) if history.is_file() else None
        dump(history,{'file':name,'sha256':digest})
        if previous and mods.resolve()==default_mods().resolve():
            pattern=r'ANR_A-4\.\d+\.\d+\.\d+\.zip' if channel=='Alpha' else r'ANR-4\.\d+\.\d+\.\d+\.zip'
            oldname=previous['file']
            require(re.fullmatch(pattern,oldname),'Invalid old ZIP journal')
            old=mods/oldname
            if oldname!=name and old.exists() and '4.0.5.0' not in oldname:
                require(old.is_file() and not old.is_symlink() and sha(old.read_bytes())==previous['sha256'],
                        'Old ZIP changed outside ANR; kept for review')
                old.unlink()

def finish(args, repo, obj):
    local_only(); locked(repo,args.token)
    compiled=read(obj/'ANR.compiled.json')
    require(compiled['token']==args.token,'No successful compiler result for this Build')
    require(snapshot(repo)==compiled['snapshot'],'Sources changed during build')
    value=compiled['version']; channel='Stable' if args.configuration=='STABLE' else 'Alpha'
    release=args.configuration in ('ALPHA','STABLE')
    bindir=Path(args.bin).resolve()
    require(sha((bindir/'AdaptiveRoads.dll').read_bytes())==compiled['dll_sha256'],'bin output differs from compiled DLL')
    dest=state(repo)/'prepared'/value
    require(not dest.exists(),'A prepared package with this version already exists; use retry, never Build to repackage')
    pending=state(repo)/'prepared'/('.pending-'+args.token); pending.mkdir(parents=True,exist_ok=False)
    content=pending/'Content'; content.mkdir()
    for name in sorted(BASE_FILES):
        if name=='FbxFormatConverter.exe': p=repo/'libs'/name
        elif name.endswith('.png'): p=bindir/'Resources'/name
        else: p=bindir/name
        require(p.is_file(),'Missing required payload: '+name)
        shutil.copyfile(p,content/name)
    if os.name=='nt':
        converter=Path(args.pdb2mdb)
        require(converter.is_file(),'pdb2mdb not found')
        mdb=bindir/'AdaptiveRoads.dll.mdb'
        if mdb.exists(): mdb.unlink()
        subprocess.run([str(converter),str(bindir/'AdaptiveRoads.dll')],check=True)
        require(mdb.is_file(),'pdb2mdb did not produce MDB')
        shutil.copyfile(mdb,content/mdb.name)
    payload=[{'file':p.name,'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())} for p in sorted(content.iterdir())]
    m={'schema':1,'name':'Adaptive Networks Remain'+(' Alpha' if channel=='Alpha' else ''),
       'version':value,'channel':channel,'configuration':args.configuration,
       'source_commit':compiled['snapshot']['source_commit'],
       'kiancommons_commit':compiled['snapshot']['kiancommons_commit'],'kiancommons_repository':KC_REPO,
       'source_state':compiled['snapshot']['source_state'],'build_utc':compiled['build_utc'],
       'toolchain':args.toolchain,'game_assemblies':compiled['game_assemblies'],
       'dll_sha256':compiled['dll_sha256'],'payload':payload}
    if release:
        (content/'BUILD_INFO.md').write_text('# '+m['name']+' '+value+'\n\n'
             'Paczkę przygotowano po kompilacji. Wynik testów gry właściciel zatwierdza osobno.\n\n'
             '```json\n'+json.dumps(m,indent=2,ensure_ascii=False)+'\n```\n',encoding='utf-8',newline='\n')
    gh=pending/'GITHUB'; gh.mkdir(); name=package_name(channel,value)
    with zipfile.ZipFile(gh/name,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for p in sorted(content.iterdir()): z.write(p,p.name)
    b=(gh/name).read_bytes(); side=sha(b)+'  '+name+'\n'
    (gh/name.replace('.zip','.sha256')).write_text(side,encoding='ascii',newline='\n')
    if release: validate_zip(b,side.encode(),sha(b),channel,value,m['source_commit'],public=False)
    else:
        with zipfile.ZipFile(gh/name) as z:
            require(z.testzip() is None,'Debug ZIP CRC failure')
            require(set(z.namelist())=={p['file'] for p in payload},'Debug payload mismatch')
            require(all(sha(z.read(p['file']))==p['sha256'] for p in payload),'Debug payload hash mismatch')
    receipt={**compiled,**m,'zip_sha256':sha(b)}
    if a_preview:=args.approved_preview:
        preview=Path(a_preview).read_bytes()
        require(preview.startswith(b'\x89PNG\r\n\x1a\n'),'Approved preview must be a PNG')
        (pending/'PreviewImage.png').write_bytes(preview)
        receipt['preview_sha256']=sha(preview)
    dump(pending/'VERIFICATION/build.json',receipt)
    pending.rename(dest)
    if release:
        require(args.archive_enabled=='true','Release archive cannot be silently omitted')
        archive(dest,args.archive_root)
    deploy=args.deploy_dir or str(default_mods()/m['name'])
    deploy_and_zip(dest,deploy,args.deploy_enabled=='true',args.zip_enabled=='true')
    print('Prepared verified package:',dest)

def main():
    p=argparse.ArgumentParser()
    p.add_argument('command',choices=['init','preflight','reserve','capture','finish','unlock','retry'])
    for name,default in [('repo','.'),('obj',''),('token',''),('seed',''),('configuration',''),('mode','Local'),
                         ('ci-version',''),('core-file',''),('core-sha',''),('intermediate',''),('bin',''),
                         ('pdb2mdb',''),('toolchain',''),('deploy-dir',''),('archive-root',''),
                         ('deploy-enabled','true'),('zip-enabled','true'),('archive-enabled','true'),
                         ('package',''),('confirmation',''),('approved-preview','')]:
        p.add_argument('--'+name,default=default)
    a=p.parse_args(); repo=Path(a.repo).resolve(); obj=Path(a.obj).resolve() if a.obj else None
    if a.command=='init':
        local_only(); version(a.seed)
        require(a.confirmation=='BASELINE VERIFIED','Explicit baseline verification is required')
        s=state(repo); require(not s.exists(),'Ledger already exists: never reset it')
        s.mkdir()
        dump(s/'reservations'/'.'.join(a.seed.split('.')[:3])/(a.seed+'.json'),{'baseline':True},True)
        dump(s/'initialized.json',{'baseline':a.seed,'created_utc':utc()},True)
        return
    if a.command=='preflight':
        require(a.configuration in ('Debug','ALPHA','STABLE'),'Unknown managed configuration')
        require(Path(a.core_file).is_file() and sha(Path(a.core_file).read_bytes())==a.core_sha,
                'Installed CoreCompile changed: regenerate and review override before Build')
        if ci(): require(a.mode=='Fixed' and VERSION.fullmatch(a.ci_version),'CI is blocked unless explicit Fixed version is supplied')
        else:
            require(a.mode=='Local','Local build must allocate a new version')
        return
    if a.command=='unlock':
        if ci(): return
        require(a.confirmation in ('','NO BUILD IS RUNNING'),'Invalid unlock confirmation')
        unlock(repo,a.token); return
    if a.command=='reserve':
        version(a.seed); require(obj is not None,'Missing obj directory')
        require(a.seed.endswith('.0'),'Keep B=0 in the Version line template; actual B belongs to the ledger')
        require(a.configuration in ('Debug','ALPHA','STABLE'),'Invalid compilation configuration')
        snap=snapshot(repo)
        refs=(obj/'ANR.references.txt').read_text(encoding='utf-8-sig').splitlines()
        games=[]
        for name in ('Assembly-CSharp.dll','ColossalManaged.dll','ICities.dll','UnityEngine.dll','UnityEngine.UI.dll'):
            candidates={str(Path(x).resolve()) for x in refs if Path(x).name==name}
            require(len(candidates)==1,'Cannot identify actual compiler reference: '+name)
            b=Path(candidates.pop()).read_bytes()
            games.append({'file':name,'sha256':sha(b),'assembly_version':managed_metadata(b)['assembly']})
        if ci():
            require(a.mode=='Fixed','CI cannot use local version allocation')
            value=version(a.ci_version)
            require(value.rsplit('.',1)[0]==a.seed.rsplit('.',1)[0],'CI line differs from Version in project')
        else:
            require(a.mode=='Local','Wrong local mode'); lock(repo,a.token)
            line=a.seed.rsplit('.',1)[0]; ledger=state(repo)/'reservations'/line; ledger.mkdir(exist_ok=True)
            used=[int(x.stem.rsplit('.',1)[1]) for x in ledger.glob('*.json')]
            n=max(used)+1 if used else 0
            require(n<=65534,'B exhausted: owner must choose a new V/P line')
            value=line+'.'+str(n)
            dump(ledger/(value+'.json'),{'token':a.token,'configuration':a.configuration,'utc':utc()},True)
        obj.mkdir(parents=True,exist_ok=True)
        (obj/'ANR.Version.txt').write_text(value+'\n',encoding='utf-8',newline='\n')
        attrs='\n'.join('[assembly: System.Reflection.'+name+'("'+value+'")]'
                        for name in ('AssemblyVersion','AssemblyFileVersion','AssemblyInformationalVersion'))+'\n'
        (obj/'ANR.Version.g.cs').write_text(attrs,encoding='utf-8',newline='\n')
        dump(obj/'ANR.attempt.json',{'token':a.token,'version':value,'configuration':a.configuration,
             'build_utc':utc(),'snapshot':snap,'game_assemblies':games})
        return
    if a.command=='capture':
        r=read(obj/'ANR.attempt.json'); require(r['token']==a.token,'Compile reservation mismatch')
        b=Path(a.intermediate).read_bytes(); md=managed_metadata(b)
        require(all(md.get(k)==r['version'] for k in ('assembly','file','informational')),'Compiled DLL version mismatch')
        require(md.get('configuration')==r['configuration'],'Compiled configuration mismatch')
        require(snapshot(repo)==r['snapshot'],'Sources changed while compiling')
        r['dll_sha256']=sha(b); dump(obj/'ANR.compiled.json',r); return
    if a.command=='finish':
        try: finish(a,repo,obj)
        finally: unlock(repo,a.token)
        return
    if a.command=='retry':
        local_only(); token=uuid.uuid4().hex; lock(repo,token)
        try:
            package=Path(a.package).resolve(); r,name=check_release(package)
            archive(package,a.archive_root)
            deploy_and_zip(package,a.deploy_dir or str(default_mods()/r['name']))
        finally: unlock(repo,token)

if __name__=='__main__':
    try: main()
    except Exception as e:
        print('ANR STOP:',type(e).__name__,str(e),file=sys.stderr); sys.exit(1)
