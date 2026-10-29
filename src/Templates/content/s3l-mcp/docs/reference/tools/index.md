# Tools

<!--#if (jobs) -->
2 tools in 2 families.
<!--#else -->
1 tool in 1 family.
<!--#endif -->
The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.

| Tool | Family | What it does |
| --- | --- | --- |
| [`tmplshort_diagnose`](diagnostics.md#tmplshort_diagnose) | Diagnostics | Say what state this server is in: its name, its version and the runtime it runs on. |
<!--#if (jobs) -->
| [`tmplshort_work`](work.md#tmplshort_work) | Work | Count to a number, a step every tenth of a second, as a job: answers with the count when it ends within the wait, or with the running job to read as a resource. |
<!--#endif -->
