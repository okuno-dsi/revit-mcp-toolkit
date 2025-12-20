# get_rooms

- �J�e�S��: Room
- �ړI: ���̃R�}���h�́wget_rooms�x���擾���܂��B

## �T�v
���̃R�}���h�� JSON-RPC ��ʂ��Ď��s����A�ړI�ɋL�ڂ̏������s���܂��B�g�����̃Z�N�V�������Q�l�Ƀ��N�G�X�g���쐬���Ă��������B

## �g����
- ���\�b�h: get_rooms

### �p�����[�^
| ���O | �^ | �K�{ | ����l |
|---|---|---|---|
| compat | bool | ������/�󋵂ɂ�� | false |
| count | int | ������/�󋵂ɂ�� |  |
| level | string | ������/�󋵂ɂ�� |  |
| nameContains | string | ������/�󋵂ɂ�� |  |
| skip | int | ������/�󋵂ɂ�� | 0 |

### ���N�G�X�g��
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_rooms",
  "params": {
    "compat": false,
    "count": 0,
    "level": "...",
    "nameContains": "...",
    "skip": 0
  }
}
```

## �֘A�R�}���h
## �֘A�R�}���h
- summarize_rooms_by_level
- validate_create_room
- get_room_params
- set_room_param
- get_room_boundary
- create_room
- delete_room
- 

