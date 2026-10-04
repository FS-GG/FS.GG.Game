from pathlib import Path
import json
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent
project = ET.parse(root / 'Consumer.fsproj').getroot()
for tag in ('ProjectReference', 'Reference', 'Import'):
    assert not project.findall('.//' + tag), f'consumer must contain no {tag}'
for source in project.findall('.//Compile'):
    path = (root / source.attrib['Include']).resolve()
    assert path.parent == root and path.is_file(), 'consumer source must be copied and local'
assets = json.loads((root / 'obj/project.assets.json').read_text())
expected = {'FS.GG.Game.Physics.Box2D/0.17.0', 'FS.GG.Game.Core/0.17.0', 'Box2D.NET/3.1.654', 'FSharp.Core/10.1.302'}
assert set(assets['libraries']) == expected, set(assets['libraries'])
assert all(row['type'] == 'package' for row in assets['libraries'].values()), 'repository project dependency present'
packages = Path(assets['project']['restore']['packagesPath'])
ns = {'n': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
def dependency_ids(package, version):
    path = packages / package.lower() / version / (package.lower() + '.nuspec')
    tree = ET.parse(path)
    return {node.attrib['id'] for node in tree.iter() if node.tag.rsplit('}', 1)[-1] == 'dependency'}
assert dependency_ids('FS.GG.Game.Core', '0.17.0') == {'FSharp.Core'}, 'Core runtime boundary changed'
assert dependency_ids('FS.GG.Game.Physics.Box2D', '0.17.0') == {'FSharp.Core', 'FS.GG.Game.Core', 'Box2D.NET'}, 'adapter runtime boundary changed'
print('package-only boundary passed: exact four-package closure, Core BCL/FSharp.Core, adapter has no rendering dependency')
