set -e
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf -
cd /work
export ERP_TEST_PASSWORD_ITERATIONS=10000 DOTNET_TieredPGO=0
dotnet restore Erp.slnx --verbosity quiet
dotnet build Erp.slnx -c Release --no-restore --verbosity quiet 2>&1 | tail -5
dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
  --filter "(FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1OutsideProcessStateTests|FullyQualifiedName~G1HttpIsolationTests|FullyQualifiedName~G1NonInterferenceTests|FullyQualifiedName~G2WriteSetTests|FullyQualifiedName~CurrencyWriteSetTests)&Process!=self-http&Process!=self-company&Process!=self-noninterference" \
  --logger "trx;LogFilePrefix=plant2" --logger "console;verbosity=normal" --results-directory /out/trx
