# Snail.Web.Template

TEMPLATE_DESCRIPTION

```bash
dotnet run --project src            # http://localhost:5080
dotnet test
```

<!--#if (IsApi) -->
`GET /api/greetings/{name}` answers a greeting, limited per client. `GET /health` answers while the application runs,
`GET /health/ready` asks what it depends on.
<!--#else -->
The pages are Blazor Server components under `src/Components`, styled with Tailwind from `src/Styles/app.css` on every
build. `GET /health` answers while the application runs, `GET /health/ready` asks what it depends on.
<!--#endif -->

## Configuration

`appsettings.json`, then variables named `Section__Key`, which win.

| Section | What it says |
| --- | --- |
| `Proxy:Networks` | The networks the proxy in front connects from, in CIDR form; only their X-Forwarded headers are believed. |
| `RateLimit` | `PermitLimit` requests per client address in a window of `WindowSeconds`. |
<!--#if (UsesCookie) -->
| `DataProtection:KeysPath` | Where the keys protecting the sign-in cookie are kept: a volume, so a new container reads the old cookies. |
<!--#endif -->
<!--#if (UsesJwt) -->
| `Jwt` | The issuer, audience and signing key of the bearer tokens ([Snail.Toolkit.Authentication.JwtBearer](https://www.nuget.org/packages/Snail.Toolkit.Authentication.JwtBearer)). |
<!--#endif -->
<!--#if (mongo) -->
| `Mongodb` | `Connection` and `Database` ([Snail.Toolkit.Mongodb](https://www.nuget.org/packages/Snail.Toolkit.Mongodb)). |
<!--#endif -->
<!--#if (rabbit) -->
| `RabbitMQ` | The broker ([Snail.Toolkit.RabbitMQ](https://www.nuget.org/packages/Snail.Toolkit.RabbitMQ)). |
<!--#endif -->
<!--#if (vault) -->
| `Vault` | When set, the secrets read from HashiCorp Vault into the configuration ([Snail.Toolkit.HashiCorp.Vault](https://www.nuget.org/packages/Snail.Toolkit.HashiCorp.Vault)). |
<!--#endif -->

## Running it

The image is built on every push and pushed to `ghcr.io/<owner>/tmplimage` from a version tag (`git tag v0.1.0`).
<!--#if (UsesCompose) -->
`docker compose up --build` runs it behind traefik, on the external network `proxy`.
<!--#else -->
`deploy/nomad/tmplimage.nomad.hcl` runs it as a Nomad job behind traefik.
<!--#endif -->

Built on [Snail.Sdk](https://github.com/orldev/Snail.Sdk): the rules, the shared workflows, the conformance check.
