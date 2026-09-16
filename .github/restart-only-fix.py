from pathlib import Path

p = Path('Model/Vanilla/VanillaCharacterBinding.cs')
raw = p.read_bytes()
crlf = b'\r\n' in raw
text = raw.decode('utf-8').replace('\r\n', '\n')
for line in (
    '            runtime.AutobattleRestartAttempts = 0;\n',
    '            runtime.AutobattleRestartInProgress = false;\n',
    '            runtime.AutobattleRecoveryExhausted = false;\n',
):
    assert text.count(line) == 1, line
    text = text.replace(line, '')
if crlf:
    text = text.replace('\n', '\r\n')
p.write_bytes(text.encode('utf-8'))
print('Removed obsolete finite restart state from character binding.')
