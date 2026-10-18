using System.Text.Json;
using Erp.Kernel.Hosting;
using Erp.Kernel.Money;
var usd = new CurrencyCode("USD");
var line = Money.Create(1.00m, usd, 3.6725m, CurrencyCode.Aed);
var total = line.Add(line).Add(line);
Console.WriteLine($"r10 gap: 3 x USD 1.00 @3.6725 -> unrounded base {total.BaseAmount}, stated {total.State().BaseAmount}");
// Invariant bypass 1: object initialiser on the parameterless struct constructor.
var forged = new Money { Amount = 100m, Currency = "USD", ExchangeRate = 0m, BaseAmount = 999m, BaseCurrency = "AED" };
Console.WriteLine($"object initialiser: amount {forged.Amount} {forged.Currency} rate {forged.ExchangeRate} base {forged.BaseAmount} (rate 0 accepted, base != amount x rate)");
// Invariant bypass 2: with-expression on a valid money.
var edited = line with { BaseAmount = 50m };
Console.WriteLine($"with-expression: amount {edited.Amount} rate {edited.ExchangeRate} base {edited.BaseAmount}");
// Invariant bypass 3: JSON body read with the platform's options.
var options = new JsonSerializerOptions(); ErpPlatform.ConfigureJson(options);
var read = JsonSerializer.Deserialize<Money>("""{"amount":"10.123456","currency":"usd","exchangeRate":"-2","baseAmount":"1","baseCurrency":"AED"}""", options);
Console.WriteLine($"JSON read: amount {read.Amount} currency '{read.Currency}' rate {read.ExchangeRate} base {read.BaseAmount} stated {read.IsStated}");
try { Console.WriteLine($"Add on JSON money: {read.Add(read).BaseAmount}"); } catch (Exception e) { Console.WriteLine("Add threw " + e.GetType().Name + ": " + e.Message); }
var dflt = default(Money);
try { Console.WriteLine($"default(Money).State(): {dflt.State()}"); } catch (Exception e) { Console.WriteLine("default(Money).State() threw " + e.GetType().Name + ": " + e.Message); }
