"""Shared ANR build and packaging validation utilities.
Python 3.11+, standard library; never executes mod DLLs.
"""
import hashlib
import io
import json
import re
import stat
import struct
import zipfile

OWNER = 'SirSheikhsPears'
REPO = OWNER + '/AdaptiveNetworks'
KC_REPO = OWNER + '/KianCommons'
VERSION = re.compile(r'4\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\Z')
SHA = re.compile(r'[0-9a-f]{64}\Z')
COMMIT = re.compile(r'[0-9a-f]{40}\Z')
BASE_FILES = {'AdaptiveRoads.dll', 'PrefabMetadata.dll', 'PrefabMetadataAPI.dll',
              'CitiesHarmony.API.dll', 'UnifiedUILib.dll', 'NS.Helpers.dll',
              'FbxFormatConverter.exe', 'NS.png', 'PreviewImage.png', 'uui_vbs.png', 'uui_ar.png'}

def require(ok, message):
    if not ok:
        raise ValueError(message)

def sha(data):
    return hashlib.sha256(data).hexdigest()

def version(value):
    require(isinstance(value, str) and VERSION.fullmatch(value), 'Invalid four-part version')
    require(all(int(p) <= 65534 for p in value.split('.')), 'Version component exceeds 65534')
    return value

def package_name(channel, value):
    version(value)
    require(channel in ('Alpha', 'Stable'), 'Invalid channel')
    return ('ANR_A-' if channel == 'Alpha' else 'ANR-') + value + '.zip'

def managed_metadata(data):
    """Read ECMA-335 Assembly table and simple assembly string attributes; no CLR load."""
    def u16(p): return struct.unpack_from('<H', data, p)[0]
    def u32(p): return struct.unpack_from('<I', data, p)[0]
    require(data[:2] == b'MZ', 'Not PE')
    pe = u32(0x3c)
    require(data[pe:pe+4] == b'PE\0\0', 'Invalid PE signature')
    opt = pe + 24
    magic = u16(opt)
    require(magic in (0x10b, 0x20b), 'Unsupported PE format')
    dd = opt + (96 if magic == 0x10b else 112)
    sections = opt + u16(pe+20)
    def rva(r):
        for i in range(u16(pe+6)):
            p = sections + 40*i
            size, va, rawsize, raw = struct.unpack_from('<IIII', data, p+8)
            if va <= r < va + max(size, rawsize):
                require(r-va < rawsize, 'RVA outside raw data')
                return raw+r-va
        raise ValueError('Unmapped RVA')
    cli = rva(u32(dd+14*8))
    root = rva(u32(cli+8))
    require(data[root:root+4] == b'BSJB', 'Invalid CLI metadata')
    p = (root+16+u32(root+12)+3) & ~3
    streams_count = u16(p+2)
    p += 4
    streams = {}
    for _ in range(streams_count):
        offset, length = struct.unpack_from('<II', data, p)
        end = data.index(0, p+8)
        name = data[p+8:end].decode('ascii')
        streams[name] = (root+offset, length)
        p = (end+4) & ~3
    t = streams.get('#~', streams.get('#-'))
    require(t is not None, 'Missing metadata tables')
    pos = t[0]
    heaps = data[pos+6]
    valid = struct.unpack_from('<Q', data, pos+8)[0]
    p = pos+24
    rows = [0]*64
    for i in range(64):
        if valid & (1 << i):
            rows[i] = u32(p); p += 4
    codes = {
        'ResolutionScope': (2,[0,26,35,1]), 'TypeDefOrRef': (2,[2,1,27]),
        'MemberRefParent': (3,[2,1,26,6,27]), 'HasConstant': (2,[4,8,23]),
        'HasCustomAttribute': (5,[6,4,1,2,8,9,10,0,14,23,20,17,26,27,32,35,38,39,40,42,44,43]),
        'CustomAttributeType': (3,[None,None,6,10,None]), 'HasFieldMarshal': (1,[4,8]),
        'HasDeclSecurity': (2,[2,6,32]), 'HasSemantics': (1,[20,23]),
        'MethodDefOrRef': (1,[6,10]), 'MemberForwarded': (1,[4,6]),
        'Implementation': (2,[38,35,39]), 'TypeOrMethodDef': (1,[2,6])}
    specs = [
        'h s g g g','ResolutionScope s s','i s s TypeDefOrRef t4 t6','t4','h s b',
        't6','i h h s b t8','t8','h h s','t2 TypeDefOrRef','MemberRefParent s b',
        'h HasConstant b','HasCustomAttribute CustomAttributeType b','HasFieldMarshal b',
        'h HasDeclSecurity b','h i t2','i t4','b','t2 t20','t20','h s TypeDefOrRef',
        't2 t23','t23','h s b','h t6 HasSemantics','t2 MethodDefOrRef MethodDefOrRef',
        's','b','h MemberForwarded s t26','i t4','i i','i',
        'i h h h h i b s s','i','i i i','h h h h i b s s b','i t35','i i i t35',
        'i s b','i i s s Implementation','i i s Implementation','t2 t2',
        'h h TypeOrMethodDef s','MethodDefOrRef b','t42 TypeDefOrRef']
    def width(f):
        if f in ('h','i'): return {'h':2,'i':4}[f]
        if f in ('s','g','b'): return 4 if heaps & {'s':1,'g':2,'b':4}[f] else 2
        if f.startswith('t'): return 4 if rows[int(f[1:])] >= 65536 else 2
        bits, tables = codes[f]
        return 4 if max(rows[x] for x in tables if x is not None) >= (1 << (16-bits)) else 2
    offsets, widths = {}, {}
    for i,spec in enumerate(specs):
        offsets[i] = p
        widths[i] = [width(f) for f in spec.split()]
        p += rows[i]*sum(widths[i])
    require(p <= t[0]+t[1], 'Invalid table bounds')
    def row(table, n):
        require(1 <= n <= rows[table], 'Invalid table index')
        at = offsets[table]+(n-1)*sum(widths[table]); out=[]
        for w in widths[table]:
            out.append(int.from_bytes(data[at:at+w], 'little')); at+=w
        return out
    def string(n):
        start, size = streams['#Strings']; require(n < size, 'Invalid string')
        return data[start+n:data.index(0,start+n,start+size)].decode('utf-8')
    def compressed(buf, at):
        b=buf[at]
        if b < 128: return b,at+1
        if b < 192: return ((b&63)<<8)|buf[at+1],at+2
        require(b < 224, 'Invalid compressed integer')
        return ((b&31)<<24)|(buf[at+1]<<16)|(buf[at+2]<<8)|buf[at+3],at+4
    def blob(n):
        start,size=streams['#Blob']; require(n < size, 'Invalid blob')
        length,at=compressed(data,start+n)
        require(at+length <= start+size, 'Invalid blob bounds')
        return data[at:at+length]
    require(rows[32] == 1, 'Exactly one Assembly row required')
    a=row(32,1)
    result={'assembly':'.'.join(map(str,a[1:5])), 'name':string(a[7])}
    names={'AssemblyFileVersionAttribute':'file','AssemblyInformationalVersionAttribute':'informational',
           'AssemblyConfigurationAttribute':'configuration'}
    for n in range(1,rows[12]+1):
        parent,kind,value=row(12,n)
        if parent != ((1<<5)|14) or kind&7 != 3: continue
        member=row(10,kind>>3)
        if member[0]&7 != 1: continue
        tr=row(1,member[0]>>3)
        if string(tr[2]) != 'System.Reflection' or string(tr[1]) not in names: continue
        key=names[string(tr[1])]
        require(key not in result, 'Duplicate assembly attribute')
        b=blob(value); require(b[:2] == b'\x01\x00', 'Bad attribute prolog')
        length,at=compressed(b,2)
        require(at+length+2 <= len(b), 'Truncated attribute')
        result[key]=b[at:at+length].decode('utf-8')
    return result

def validate_zip(data, sidecar, expected, channel, value, source_commit, public=True):
    name=package_name(channel,value)
    require(SHA.fullmatch(expected) is not None and sha(data)==expected, 'ZIP SHA-256 mismatch')
    require(sidecar.decode('ascii').strip() == expected+'  '+name, 'Invalid external SHA-256 file')
    require(len(data) <= 25*1024*1024, 'Package exceeds proposed 25 MiB transport limit')
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        infos=z.infolist(); names=[i.filename for i in infos]
        require(len(names)==len(set(n.casefold() for n in names)), 'Duplicate/case-colliding ZIP names')
        require(len(names)<=20 and sum(i.file_size for i in infos)<=100*1024*1024, 'ZIP size limit')
        allowed=BASE_FILES|{'AdaptiveRoads.dll.mdb','BUILD_INFO.md'}
        require(set(names) <= allowed and BASE_FILES|{'BUILD_INFO.md'} <= set(names), 'Unexpected/missing payload')
        for i in infos:
            require(not i.is_dir() and not i.flag_bits&1 and not stat.S_ISLNK(i.external_attr>>16), 'Unsafe ZIP entry')
        manifest=z.read('BUILD_INFO.md').decode('utf-8')
        blocks=re.findall(r'```json\n(.*?)\n```',manifest,re.S)
        require(len(blocks)==1, 'BUILD_INFO.md requires one JSON block')
        m=json.loads(blocks[0])
        require(m['schema']==1 and m['version']==value and m['channel']==channel, 'Manifest identity mismatch')
        require(m['configuration']==('ALPHA' if channel=='Alpha' else 'STABLE'), 'Wrong release configuration')
        require(m['name']==('Adaptive Networks Remain Alpha' if channel=='Alpha' else 'Adaptive Networks Remain'), 'Wrong mod name')
        require(m['source_commit']==source_commit and COMMIT.fullmatch(source_commit), 'Source commit mismatch')
        require(COMMIT.fullmatch(m['kiancommons_commit']) and m['kiancommons_repository']==KC_REPO, 'KianCommons provenance mismatch')
        require(m['source_state'] in ('clean','dirty'), 'Invalid source state')
        if public: require(m['source_state']=='clean', 'Dirty source cannot be published')
        require(re.fullmatch(r'\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ',m['build_utc']), 'Invalid build timestamp')
        require(isinstance(m['toolchain'],str) and 0<len(m['toolchain'])<=200, 'Missing toolchain')
        require(isinstance(m['game_assemblies'],list) and len(m['game_assemblies'])>=4, 'Missing game evidence')
        required_game={'Assembly-CSharp.dll','ColossalManaged.dll','ICities.dll','UnityEngine.dll'}
        require(required_game <= {g['file'] for g in m['game_assemblies']}, 'Missing primary game libraries')
        for g in m['game_assemblies']:
            require(re.fullmatch(r'[A-Za-z0-9_.-]+\.dll',g['file']) and SHA.fullmatch(g['sha256']), 'Invalid game evidence')
        payload=m['payload']; require(isinstance(payload,list), 'Missing payload inventory')
        require(len(payload)==len({p['file'] for p in payload}), 'Duplicate payload entries')
        require({p['file'] for p in payload}==set(names)-{'BUILD_INFO.md'}, 'Payload inventory mismatch')
        for p in payload:
            b=z.read(p['file'])
            require(p['sha256']==sha(b) and p['bytes']==len(b), 'Payload hash/size mismatch')
        dll=z.read('AdaptiveRoads.dll'); md=managed_metadata(dll)
        require(md['name']=='AdaptiveRoads', 'Wrong assembly name')
        require(all(md.get(k)==value for k in ('assembly','file','informational')), 'DLL version mismatch')
        require(md.get('configuration')==m['configuration'], 'DLL configuration mismatch')
        require(m['dll_sha256']==sha(dll), 'DLL hash mismatch')
        return m
