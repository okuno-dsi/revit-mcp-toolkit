# duplicate_sanitary_fixture_type

- Category: ElementOps
- Purpose: Duplicate a Sanitary Fixture FamilySymbol (type).

## Usage
- Method: duplicate_sanitary_fixture_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| sourceTypeId | int | yes |  |
| newTypeName | string | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_sanitary_fixture_type",
  "params": {
    "sourceTypeId": 100001,
    "newTypeName": "MyFixtureType_Copy"
  }
}
```

## Related
- get_sanitary_fixture_types
- delete_sanitary_fixture_type
- change_sanitary_fixture_type

