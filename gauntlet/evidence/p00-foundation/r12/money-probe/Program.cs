using System.Text.Json;
using Erp.Kernel.Hosting;
using Erp.Kernel.Money;
// Critic p00 r12: does Money still accept a figure that breaks its invariants?
var usd = new CurrencyCode("USD");
var line = Money.Create(1.00m, usd, 3.6725m, CurrencyCode.Aed);
var total = line.Add(line).Add(line);
Console.WriteLine($"3 x USD 1.00 @3.6725 -> base {total.BaseAmount}, stated {total.State().BaseAmount}");
var options = new JsonSerializerOptions(); ErpPlatform.ConfigureJson(options);
void J(string label, string json) {
  try { var m = JsonSerializer.Deserialize<Money>(json, options); Console.WriteLine($"{label}: ACCEPTED amount {m.Amount} {m.Currency} rate {m.ExchangeRate} base {m.BaseAmount} {m.BaseCurrency}"); }
  catch (Exception e) { Console.WriteLine($"{label}: refused {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
}
J("good", """{"amount":"10.00","currency":"USD","exchangeRate":"3.6725","baseAmount":"36.7250","baseCurrency":"AED"}""");
J("lowercase currency", """{"amount":"10.00","currency":"usd","exchangeRate":"3.6725","baseAmount":"36.7250","baseCurrency":"AED"}""");
J("negative rate", """{"amount":"10.00","currency":"USD","exchangeRate":"-2","baseAmount":"-20","baseCurrency":"AED"}""");
J("base disagrees", """{"amount":"10.00","currency":"USD","exchangeRate":"3.6725","baseAmount":"1","baseCurrency":"AED"}""");
J("zero rate", """{"amount":"10.00","currency":"USD","exchangeRate":"0","baseAmount":"0","baseCurrency":"AED"}""");
J("numbers not strings", """{"amount":10.00,"currency":"USD","exchangeRate":3.6725,"baseAmount":36.725,"baseCurrency":"AED"}""");
J("float exponent", """{"amount":"1e2","currency":"USD","exchangeRate":"3.6725","baseAmount":"367.25","baseCurrency":"AED"}""");
J("missing base", """{"amount":"10.00","currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""");
J("duplicate amount", """{"amount":"10.00","amount":"99","currency":"USD","exchangeRate":"3.6725","baseAmount":"36.7250","baseCurrency":"AED"}""");
J("same currency rate 2", """{"amount":"10.00","currency":"AED","exchangeRate":"2","baseAmount":"20","baseCurrency":"AED"}""");
J("unknown currency", """{"amount":"10.00","currency":"XYZ","exchangeRate":"1","baseAmount":"10","baseCurrency":"AED"}""");
J("JPY fraction", """{"amount":"10.5","currency":"JPY","exchangeRate":"0.025","baseAmount":"0.2625","baseCurrency":"AED"}""");
var dflt = default(Money);
try { Console.WriteLine($"default(Money).Add: {dflt.Add(line)}"); } catch (Exception e) { Console.WriteLine("default(Money).Add refused " + e.GetType().Name); }
try { Console.WriteLine($"line.Add(default): {line.Add(dflt)}"); } catch (Exception e) { Console.WriteLine("line.Add(default) refused " + e.GetType().Name); }
try { Console.WriteLine($"default ser: {JsonSerializer.Serialize(dflt, options)}"); } catch (Exception e) { Console.WriteLine("Serialize(default) refused " + e.GetType().Name); }
try { var eur = Money.Create(5m, new CurrencyCode("EUR"), 4m, CurrencyCode.Aed); Console.WriteLine($"USD+EUR: {line.Add(eur)}"); } catch (Exception e) { Console.WriteLine("USD+EUR refused " + e.GetType().Name); }
try { var r2 = Money.Create(1m, usd, 3.67m, CurrencyCode.Aed); Console.WriteLine($"USD@3.6725 + USD@3.67: {line.Add(r2)} base {line.Add(r2).BaseAmount} rate {line.Add(r2).ExchangeRate}"); } catch (Exception e) { Console.WriteLine("different-rate Add refused " + e.GetType().Name + ": " + e.Message); }
try { Console.WriteLine($"Create(1e-11): {Money.Create(0.00000000001m, usd, 3.6725m, CurrencyCode.Aed)}"); } catch (Exception e) { Console.WriteLine("Create 1e-11 refused " + e.GetType().Name); }
