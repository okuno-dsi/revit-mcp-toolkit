# get_last_selection

- Category: Misc
- Purpose: Return the most recently observed selection snapshot (and the last non-empty selection).

## Usage
- Method: `get_last_selection`

### Parameters
```jsonc
{
  "maxAgeMs": 0 // optional; if >0, only returns ok:true when selection age <= maxAgeMs
}
```

