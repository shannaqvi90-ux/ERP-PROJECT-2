set -e
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf -
cd /work
export ERP_TEST_PASSWORD_ITERATIONS=10000 DOTNET_TieredPGO=0
dotnet restore Erp.slnx --verbosity quiet
dotnet build Erp.slnx -c Release --no-restore --verbosity quiet 2>&1 | tail -3
dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet --filter "FullyQualifiedName~G1TenantSourceTests|FullyQualifiedName~AuditGateTests" --logger "console;verbosity=normal" || true
dotnet test tests/Erp.Modules.Currency.Tests/Erp.Modules.Currency.Tests.csproj -c Release --no-build --verbosity quiet --logger "console;verbosity=normal" || true
