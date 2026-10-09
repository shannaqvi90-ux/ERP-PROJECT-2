# Critic p03 r7 plant L6 (run 2, alone): the identity migration adds a permissive SELECT policy on identity.roles,
# so a session bound to tenant A reads every workspace's roles (permissive policies are OR-ed with the tenant policy).
import pathlib
m = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Migrations/20261009011818_OwnCompanyRolesReadOnly.cs'); s = m.read_text()
old = """            migrationBuilder.KeepOwnRowsReadOnly("identity", "user_company_roles");"""
new = old + """
            migrationBuilder.Sql("CREATE POLICY roles_lookup ON identity.roles AS PERMISSIVE FOR SELECT USING (true);");"""
assert old in s; s = s.replace(old, new); m.write_text(s); print("L6 applied")
