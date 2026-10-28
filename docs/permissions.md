# Permissions and endpoints

This page exists so that a security reviewer can answer a client's questionnaire without reading the source. It
states what the tool reads, what it needs, and what it contacts.

## The role you need

| Scope | Role | Why |
|---|---|---|
| Subscription (or a resource group, for a resource-group scope) | **Reader** | every call is a read of resource metadata |
| — | nothing else | no write role is used, requested or needed |

Reader is the Microsoft-published role that grants `*/read`. The tool does not need `Network Contributor`,
`Contributor` or any data-plane role. It reads no key vault secret, no storage blob and no virtual machine disk.

Some reads are cheaper with more specific permissions, and some resource types are only visible with a custom
role that includes their read actions. If a type is missing from the inventory, the report's coverage section
names it — that is a permission answer, not a tool failure.

## What it reads

- Subscriptions visible to the identity (`login-status`, and the scope validation of a collection).
- Resource groups.
- Network resources through Azure Resource Graph: virtual networks, subnets, peerings, network security groups
  and their rules, route tables and routes, network interfaces, public IP addresses, private endpoints, private
  DNS zones, Azure Firewalls, Virtual WANs, virtual hubs, ExpressRoute circuits and virtual network gateways.
- Optionally, with `--resolve-links true`, the resource a NIC or private endpoint references.

## What it contacts

| Endpoint | Verb | Purpose |
|---|---|---|
| `https://management.azure.com/providers/Microsoft.ResourceGraph/resources` | `POST` | the inventory query. `POST` is the shape of that API — it submits a query and returns its result; nothing is written |
| `https://management.azure.com/subscriptions…` | `GET` | the subscription list, a single subscription, its resource groups, and the individual resource reads |

That is the complete list. The tool contacts no other host: no licence check, no telemetry endpoint, no update
check, no remote configuration.

Authentication goes through `DefaultAzureCredential`, which tries the environment, workload identity, managed
identity, Visual Studio, and the Azure CLI in that order. The token is minted for
`https://management.azure.com/.default` and held in memory.

## What it never does

- No write, delete or `POST` that changes state. The only `POST` is the Resource Graph query above.
- No upload. There is no Radivum service in the loop: the tool does not know a Radivum endpoint exists.
- No telemetry, analytics or crash reporting.
- No credential storage. Nothing is written outside the output directory, and that directory contains only the
  artifacts listed in [artifacts.md](artifacts.md).
- No access to data planes — no key vault secrets, no storage content, no VM guest access.

## Verifying it yourself

The collection code is `src/Radivum.Azure`. Every HTTP request is constructed there, the only base address is the
one in the table above, and the request count is small enough to read in one sitting. If you would rather not
trust a claim, this is the claim to check.
