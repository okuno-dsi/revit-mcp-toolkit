# set_text

- Category: AnnotationOps
- Purpose: Set the `Text` of a TextNote element.

## Usage
- Method: `set_text`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | yes |  |
| text | string | yes |  |
| refreshView | bool | no | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_text",
  "params": {
    "elementId": 123456,
    "text": "Hello",
    "refreshView": true
  }
}
```

## Related
- create_text_note
- update_text_note_parameter

