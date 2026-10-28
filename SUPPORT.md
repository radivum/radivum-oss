# Support

## Where to ask

| What | Where |
|---|---|
| A question, a "how do I…", an idea to discuss | **Discussions** on this repository |
| A defect with a reproduction | **Issues** — include the version, the command, and what you expected |
| A security problem | **Security → Report a vulnerability** (never a public issue), see [SECURITY.md](SECURITY.md) |
| A question about Radivum Pro or the Workbench | [www.radivum.com](https://www.radivum.com) — those are separate, proprietary products |

## What to include in a defect report

```
dotnet tool list --global          # the installed radivum-oss version
dotnet --version
the command line you ran (with identifiers replaced)
what you expected, and what happened
```

If the problem is in what the tool read, say which subscription-level permissions your identity has: most
"missing data" reports turn out to be a permission that was not delegated at the scope. `radivum-oss
login-status` shows the scopes your login can see.

Please do not attach a real snapshot. It describes someone's network. A synthetic reproduction from
`samples/` is usually enough to explain a problem.

## What this support is

Best effort, by the maintainer, in public. There is no service-level agreement, no paid support plan and no
guarantee of a fix by a date for the open-source edition. Radivum Pro licensing comes with its own terms.
