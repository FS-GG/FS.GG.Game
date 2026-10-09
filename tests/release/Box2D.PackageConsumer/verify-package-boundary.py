from pathlib import Path
import argparse
import json
import xml.etree.ElementTree as ET

if not __debug__:
    raise SystemExit('package boundary assertions require Python optimization to be disabled')
parser = argparse.ArgumentParser(description='Verify the isolated Box2D package consumer boundary.')
parser.add_argument('--presentation', action='store_true')
parser.add_argument('--preflight', action='store_true', help='Check copied project/source inputs before restore.')
args = parser.parse_args()
root = Path(__file__).resolve().parent
project = ET.parse(root / 'Consumer.fsproj').getroot()
for tag in ('ProjectReference', 'Reference', 'Import'):
    assert not project.findall('.//' + tag), f'consumer must contain no {tag}'
for source in project.findall('.//Compile'):
    path = root / source.attrib['Include']
    assert not path.is_symlink() and path.resolve().parent == root and path.is_file(), 'consumer source must be copied and local'
    assert 'Link' not in source.attrib, 'consumer source must not be linked'
expected_references = {'FS.GG.Game.Physics.Box2D': '[0.17.0]', 'FSharp.Core': '[10.1.302]'}
if args.presentation:
    expected_references['FS.GG.Game.Render'] = '[0.17.0]'
references = project.findall('.//PackageReference')
assert len(references) == len(expected_references), 'unexpected direct package reference count'
assert {node.attrib.get('Include'): node.attrib.get('Version') for node in references} == expected_references, 'direct package pins differ'
if args.presentation:
    assert [node.attrib['Include'] for node in project.findall('.//Compile')] == ['PortalScene.fs', 'Presentation.fs', 'Program.fs'], 'presentation compile inputs differ'
if args.preflight:
    print('package-only preflight passed: copied local sources and exact direct package pins')
    raise SystemExit(0)
assets = json.loads((root / 'obj/project.assets.json').read_text())
expected = {'FS.GG.Game.Physics.Box2D/0.17.0', 'FS.GG.Game.Core/0.17.0', 'Box2D.NET/3.1.654', 'FSharp.Core/10.1.302'}
if args.presentation:
    expected |= {'FS.GG.Game.Render/0.17.0', 'FS.GG.UI.Scene/0.31.0', 'FS.GG.UI.KeyboardInput/0.31.0'}
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
if args.presentation:
    assert dependency_ids('FS.GG.Game.Render', '0.17.0') == {'FSharp.Core', 'FS.GG.Game.Core', 'FS.GG.UI.Scene', 'FS.GG.UI.KeyboardInput'}, 'Render runtime boundary changed'
    assert dependency_ids('FS.GG.UI.Scene', '0.31.0') == {'FSharp.Core'}, 'Scene runtime boundary changed'
    assert dependency_ids('FS.GG.UI.KeyboardInput', '0.31.0') == {'FSharp.Core', 'FS.GG.UI.Scene'}, 'KeyboardInput runtime boundary changed'
    print('package-only presentation boundary passed: exact seven-package closure, no window/GPU dependency')
else:
    print('package-only boundary passed: exact four-package closure, Core BCL/FSharp.Core, adapter has no rendering dependency')
