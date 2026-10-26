"""Create a reviewable CoreCompile override from the OWNER'S installed Roslyn target.
Does not modify the source SDK target or project. No build is invoked.
Usage: python scripts/anr_generate_corecompile.py --source <actual imported target> --output <new target>
"""
import argparse
import copy
from pathlib import Path
import xml.etree.ElementTree as ET
from anr_common import require, sha

def generate(source,output):
    raw=Path(source).read_bytes(); doc=ET.fromstring(raw)
    ns=doc.tag.partition('}')[0]+'}' if '}' in doc.tag else ''
    cores=[x for x in doc if x.tag==ns+'Target' and x.get('Name')=='CoreCompile']
    require(len(cores)==1,'Expected exactly one actual CoreCompile target')
    core=copy.deepcopy(cores[0]); cscs=[x for x in core if x.tag==ns+'Csc']
    require(len(cscs)==1,'Expected one direct Csc task; review changed SDK manually')
    require(core.get('Inputs') and core.get('Outputs'),'Missing incremental Inputs/Outputs')
    require('MSBuildThisFile' not in ET.tostring(core,encoding='unicode'), 'Target has location-relative logic; manual adaptation required')
    require(cscs[0].get('Sources')=='@(Compile)','Unexpected compiler source inputs')
    csc=cscs[0]
    def strip(e):
        e.tag=e.tag.split('}')[-1]
        for ch in e: strip(ch)
    strip(core)
    base="'$(DesignTimeBuild)' != 'true' and '$(SkipCompilerExecution)' != 'true'"
    require(csc.get('Condition') == "'%(_CoreCompileResourceInputs.WithCulture)' != 'true'",
            'Unexpected Csc condition; manual review required')
    cond='('+base+')'
    root=ET.Element('Project')
    root.append(ET.Comment('Generated CoreCompile override. Roslyn source: MIT, .NET Foundation. Review after every toolchain change.'))
    pg=ET.SubElement(root,'PropertyGroup')
    ET.SubElement(pg,'ANRCoreCompileSHA256').text=sha(raw)
    helper='&quot;' # ElementTree escapes the actual quotes for XML below.
    common='"$(ANRPython)" "$(ANRRepo)/scripts/anr_build.py"'
    args=' --repo "$(ANRRepo)" --obj "$(_ANRObj)" --token "$(_ANRToken)"'
    reserve=ET.Element('Exec',{'Condition':cond,'Command':common+' reserve'+args+
            ' --seed "$(Version)" --configuration "$(Configuration)" --mode "$(ANRVersionAllocationMode)" --ci-version "$(ANRCIVersion)"'})
    refs=ET.Element('WriteLinesToFile',{'Condition':cond,'File':'$(_ANRObj)/ANR.references.txt',
            'Lines':"@(ReferencePathWithRefAssemblies->'%(FullPath)')",'Overwrite':'true','Encoding':'UTF-8'})
    lines=ET.Element('ReadLinesFromFile',{'Condition':cond,'File':'$(_ANRObj)/ANR.Version.txt'})
    ET.SubElement(lines,'Output',{'TaskParameter':'Lines','PropertyName':'_ANRAllocatedVersion'})
    props=ET.Element('PropertyGroup',{'Condition':cond+" and '$(_ANRAllocatedVersion)' != ''"})
    for key in ('Version','AssemblyVersion','FileVersion','InformationalVersion'):
        ET.SubElement(props,key).text='$(_ANRAllocatedVersion)'
    err=ET.Element('Error',{'Condition':"("+cond+") and ('$(Version)' != '$(_ANRAllocatedVersion)' or '$(AssemblyVersion)' != '$(_ANRAllocatedVersion)' or '$(FileVersion)' != '$(_ANRAllocatedVersion)' or '$(InformationalVersion)' != '$(_ANRAllocatedVersion)')",
            'Text':'ANR version properties are immutable global overrides. Remove /p:Version and related overrides.'})
    idx=list(core).index(csc)
    for x in (refs,reserve,lines,props,err): core.insert(idx,x); idx+=1
    capture=ET.Element('Exec',{'Condition':cond,'Command':common+' capture'+args+' --intermediate "@(IntermediateAssembly)"'})
    ET.SubElement(capture,'Output',{'TaskParameter':'ExitCode','PropertyName':'_ANRCscRecorded'})
    core.insert(idx+1,capture)
    # OnError must follow all normal tasks; existing error handlers retain their order.
    core.append(ET.Element('OnError',{'ExecuteTargets':'ANRUnlock','Condition':"'$(_ANRCI)' != 'true'"}))
    root.append(core); ET.indent(root,space='  ')
    output=Path(output); output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('xb') as f: f.write(ET.tostring(root,encoding='utf-8',xml_declaration=True)+b'\n')
    print('Created review copy:',output,'source SHA-256:',sha(raw))

if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('--source',required=True); p.add_argument('--output',required=True)
    a=p.parse_args(); generate(a.source,a.output)
