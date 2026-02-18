# get_rooms

- JeS: Room
- ړI: ̃R}h́wget_roomsx擾܂B

## Tv
̃R}h JSON-RPC ʂĎsAړIɋLڂ̏s܂Bg̃ZNVQlɃNGXg쐬ĂB

## g
- \bh: get_rooms

### p[^
| O | ^ | K{ | l |
|---|---|---|---|
| compat | bool | /󋵂ɂ | false |
| count | int | /󋵂ɂ |  |
| level | string | /󋵂ɂ |  |
| nameContains | string | /󋵂ɂ |  |
| skip | int | /󋵂ɂ | 0 |

### NGXg
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

## ֘AR}h
## ֘AR}h
- summarize_rooms_by_level
- validate_create_room
- get_room_params
- set_room_param
- get_room_boundary
- create_room
- delete_room
- 


