# agent_bootstrap

- �J�e�S��: MetaOps
- �ړI: ���̃R�}���h�́wagent_bootstrap�x�����s���܂��B

## �T�v
���̃R�}���h�� JSON-RPC ��ʂ��Ď��s����A�ړI�ɋL�ڂ̏������s���܂��B�g�����̃Z�N�V�������Q�l�Ƀ��N�G�X�g���쐬���Ă��������B

## �g����
- ���\�b�h: agent_bootstrap

- �p�����[�^: �Ȃ�

### ���N�G�X�g��
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "agent_bootstrap",
  "params": {}
}
```

## ���ʃ��Z�b�g�̕���
- `server`: Revit�v���Z�X�̏�܂��̃��C�e�B (`product`, `process.pid` �Ȃ�)�B
- `project`: ���O�ɐݒ肵��v���W�F�N�g/�h�L�������g���� (name, number, filePath, revitVersion, documentGuid, message �Ȃ�)�B
- `environment`: ���O�ɐݒ肵��Ԓn/�P�ʂȊw�K��� (units, activeViewId, activeViewName �Ȃ�)�B
- `document`: v���W�F�N�g�������w�K���͂��₷�A�g�p����̂������̂Q�l�ȃR���e�L�X�g�ł��B
  - `ok`, `name`, `number`, `filePath`, `revitVersion`, `documentGuid`
  - `activeViewId`, `activeViewName`
  - `units`: `{ input, internalUnits }`

���݂̃N���C�A���g�� `project.*` �や `environment.*` ���g���Ă����̂܂܁A�݌v�͂Ȃ��܂��B�V�K�̃N���C�A���g�́A`document.*` ���K�v�Ɏg�����ɂ���Ę����Ă��������B

## �֘A�R�}���h
- start_command_logging
- stop_command_logging
