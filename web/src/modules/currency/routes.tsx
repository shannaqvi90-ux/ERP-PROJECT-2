import type { RouteDef } from "../../kernel/router";
import { CurrenciesPage } from "./CurrenciesPage";
import { RatesPage } from "./RatesPage";

export const routes: RouteDef[] = [
  { path: "/currency/rates", titleKey: "currency.menu.rates", permission: "currency.rates.read", component: RatesPage },
  { path: "/currency/currencies", titleKey: "currency.menu.currencies", permission: "currency.currencies.read", component: CurrenciesPage },
];
