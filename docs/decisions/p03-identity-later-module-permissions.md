# p03 identity: roles ready for module permissions that do not exist yet

The bar's "create a user with a restricted role" asks for someone who may view and create
contacts and nothing else. Contacts arrive in wave 2 (p16), so identity must take their
permissions as they come, with nothing to change here.

## Decision

- A module declares `module.resource.action` permissions; the catalogue, the role API, the access
  view, the Administrator role (kept in step with the catalogue on every seed run) and the
  permission matrix (a block per module, a column per common action, the rest under "other") all
  read the catalogue. `tests/Erp.Modules.Identity.Tests/ModulePermissionsTests.cs` hosts a
  contacts module of the later wave's shape and proves the whole path: a "Contacts clerk" role is
  one POST, its user may view and create contacts and gets 403 on change, delete and every
  identity screen, the access view explains each permission, and a user administrator who lacks
  contacts permissions cannot hand the role out.
- **Ticking any action of a resource also ticks viewing it** (`withImpliedReads`), when the
  catalogue has a view permission for that resource and the user may grant it. A role that may
  create contacts but not see them is never what anyone means, and the restricted role is built
  with one tick fewer. Turning permissions off never removes anything else.
- The compare driver for create-restricted-user (`gauntlet/compare/drivers/ours/`) uses
  "Contacts clerk" as soon as the catalogue has those permissions and, until then, the nearest
  restricted role (Read-only), saying so in its verification details. Its set-up and clean-up
  remove the task's user through the API (possible now that a user who never signed in can be
  deleted).
