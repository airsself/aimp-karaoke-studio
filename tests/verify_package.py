"""Check an actually built .aimppack, not a source ZIP."""
from pathlib import Path
from zipfile import ZipFile
import sys
if len(sys.argv) != 3:
    raise SystemExit('Usage: python tests/verify_package.py <file.aimppack> x64|x86')
package=Path(sys.argv[1]); arch=sys.argv[2]
assert arch in ('x86','x64')
assert package.is_file()
with ZipFile(package) as z:
    names=set(z.namelist())
    prefix='AimpKaraokeStudio/' + ('x64/' if arch == 'x64' else '')
    assert prefix+'AimpKaraokeStudio.dll' in names, 'Missing native DLL at expected AIMP path'
    assert prefix+'KaraokeStudio.exe' in names, 'Missing GUI app at expected AIMP path'
    assert 'AimpKaraokeStudio/INSTALL.md' in names, 'Missing instructions'
    if arch == 'x64':
        assert 'AimpKaraokeStudio/AimpKaraokeStudio.dll' not in names, 'x64 DLL incorrectly at plugin root'
print('Package layout validated:', package, arch)
