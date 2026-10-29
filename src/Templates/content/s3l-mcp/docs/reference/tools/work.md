# Work

## <code>sample_<wbr>work</code>

Count to a number, a step every tenth of a second, as a job: answers with the count when it ends within the wait, or with the running job to read as a resource.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `upTo` | `integer` | yes | — | The number to count to, 1 to 600. |
| `wait` | `integer` | — | `60` | Seconds to wait for the count before answering with the job in flight; 0 answers at once. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "upTo": {
      "description": "The number to count to, 1 to 600.",
      "type": "integer"
    },
    "wait": {
      "description": "Seconds to wait for the count before answering with the job in flight; 0 answers at once.",
      "type": "integer",
      "default": 60
    }
  },
  "required": [
    "upTo"
  ]
}
```

</details>

