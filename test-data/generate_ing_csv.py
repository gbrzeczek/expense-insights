#!/usr/bin/env python3
"""Generate a fake ING Bank Śląski CSV export for testing.

Mirrors the quirks seen in a real export:
  - Windows-1250, ';'-delimited, LF line endings, no BOM
  - a preamble before the header: bank info, document number, generation time,
    user's name/address (PII), selected accounts with numbers, date range, and
    "Suma uznań (N)" / "Suma obciążeń (N)" totals usable as an integrity check
  - a footer: two blank lines + "Dokument ma charakter informacyjny..."
  - several accounts in one file (current + savings), told apart by "Konto";
    own transfers show up on both accounts
  - 21 fields per line: 17 named columns + 4 trailing empty ones
  - text fields double-quoted with padding spaces (" BIEDRONKA ... "), dates,
    amounts and apostrophe-wrapped ids unquoted
  - booking date can precede the transaction date (savings interest)
  - 17 named columns, duplicated "Waluta" header names
  - newest-first ordering, running "Saldo po transakcji" (includes holds)
  - pending card payments (blokady): no booking date, no transaction id,
    amount in the "Kwota blokady" column instead of "Kwota transakcji"
  - apostrophe-wrapped ids/account numbers, stray leading/trailing spaces
  - BLIK e-commerce paid through intermediaries (PayU, Cashbill...), with the
    real shop only visible at the end of the title
  - transaction id = YYYY + day-of-year(booking) + 2-digit channel + 9 digits
  - more "Szczegóły" codes: ST.ZLEC (standing order: the rent), EXPRESS (express
    transfer to a friend), PZ (transfer order: a quarterly insurance premium)
  - booked rows without a transaction number: a cluster of corrections that
    cancel out (debits and credits of equal sums), with a running balance
  - rows a little out of balance order: some same-day rows on one account are
    listed swapped, so their "Saldo" values don't chain in file order
  - frequent coffee at similar prices (Costa Coffee), which must not look like
    a subscription

Also writes a ground-truth labels file (one row per data row) so classifier
accuracy can be measured.

Usage:
  python3 generate_ing_csv.py --months 6 --seed 42 --out fake_ing.csv
  python3 generate_ing_csv.py --encoding utf-8 --delimiter '\\t' --no-preamble  # as pasted from a spreadsheet
  python3 generate_ing_csv.py --months 2 --seed 10 --overlap --out fake_ing_overlap.csv
      # two exports, fake_ing_overlap_a.csv and fake_ing_overlap_b.csv: B starts before A ends,
      # and the pending holds at the end of A are booked in B (same transaction ids)

Unverified (guessed): quoting of "Nazwa banku", "Szczegóły", "Konto" and
currency columns; header quoting; whether the "Suma" totals include pending
holds (we include them); interest/tax titles on the savings account; titles,
codes and counterparties of the rows without a number (shaped like corrections
here; in the real export they look like ordinary transactions).

The newer quirks draw from their own random stream (seeded from --seed), so the
rows that existed before them keep their values and ids.
"""
import argparse
import csv
import random
from dataclasses import dataclass, replace
from datetime import date, timedelta

HEADER = [
    "Data transakcji", "Data księgowania", "Dane kontrahenta", "Tytuł",
    "Nr rachunku", "Nazwa banku", "Szczegóły", "Nr transakcji",
    "Kwota transakcji (waluta rachunku)", "Waluta",
    "Kwota blokady/zwolnienie blokady", "Waluta",
    "Kwota płatności w walucie", "Waluta",
    "Konto", "Saldo po transakcji", "Waluta",
]

OWNER = "JAN PAWEŁ KOWALSKI"
OWNER_PREAMBLE = "KOWALSKI JAN PAWEŁ , UL. FAŁSZYWA 1 00-001 WARSZAWA"
DOCUMENT_NO = "0123456789"
MAIN_ACCOUNT = "KONTO Mobi pow.26"
SAVINGS_ACCOUNT = "Otwarte Konto Oszczędnościowe"
ING_COUNTERPARTY = "ING BANK ŚLĄSKI S.A. UL.SOKOLSKA 34 40-086 KATOWICE"
EXTRA_FIELDS = 4  # trailing empty fields on every header/data line
QUOTED_COLUMNS = {2, 3, 5, 6, 14}  # counterparty, title, bank, details, account name
CARD = "4111xx1234"
CARD_ACQUIRER = "'1915031/19730 '"

# (counterparty template, category, canonical merchant, amount range, weekly freq)
# {n} = store number. Odd spacing is intentional — it matches real exports.
CARD_MERCHANTS = [
    ("JMP S.A. BIEDRONKA {n}  KRAKOW  PO", "Spożywcze", "Biedronka", (12, 160), 2.5),
    ("LIDL KAMIENSKIEGO  KRAKOW  POL", "Spożywcze", "Lidl", (20, 220), 0.8),
    ("ZABKA Z{n} K.1  KRAKOW  POL", "Spożywcze", "Żabka", (6, 45), 1.5),
    ("KAUFLAND 01 KRAKOW  KRAKOW  POL", "Spożywcze", "Kaufland", (40, 260), 0.3),
    (" MCDONALDS 0{n}  KRAKOW  POL ", "Restauracje i jedzenie na wynos", "McDonald's", (18, 70), 0.4),
    ("STARBUCKS KRAKOW GAL  KRAKOW  POL", "Restauracje i jedzenie na wynos", "Starbucks", (14, 40), 0.3),
    ("ORLEN STACJA NR {n}  KRAKOW  POL", "Transport", "Orlen", (80, 320), 0.4),
    ("BOLT.EU/R/{n}  Tallinn  EST", "Transport", "Bolt", (14, 55), 0.4),
    (" ZNANYLEKARZ SPOLKA Z O  warszawa  P ", "Zdrowie", "ZnanyLekarz", (150, 450), 0.05),
    ("APTEKA DOZ {n}  KRAKOW  POL", "Zdrowie", "Apteka DOZ", (9, 120), 0.3),
    ("CASTORAMA KRAKOW 4  KRAKOW 30964 PO", "Dom i ogród", "Castorama", (15, 400), 0.15),
    ("IKEA KRAKOW  KRAKOW  POL", "Dom i ogród", "IKEA", (30, 700), 0.08),
    ("PEPCO {n} KRAKOW 14  KRAKOW  POL", "Dom i ogród", "Pepco", (10, 90), 0.2),
    ("ROSSMANN {n}  KRAKOW  POL", "Drogeria i uroda", "Rossmann", (12, 150), 0.4),
    (" Bershka.com               Arteixo ", "Odzież i obuwie", "Bershka", (60, 350), 0.05),
    ("RESERVED BONARKA  KRAKOW  POL", "Odzież i obuwie", "Reserved", (40, 300), 0.08),
    (" MEDIAEXPERT  WEGRZCE  POL ", "Elektronika", "Media Expert", (30, 900), 0.05),
    (" DECATHLON KRAKOW PL  KRAKOW  POL ", "Sport, hobby i rozrywka", "Decathlon", (25, 400), 0.1),
    (" Zielony Parapet  Krakow  POL ", "Sport, hobby i rozrywka", "Zielony Parapet", (30, 200), 0.05),
    # Deliberately ambiguous — the classifier should struggle here.
    ("{n} KRAKOW ZAKOPIAN  KRAKOW  POL", "Inne", "?", (8, 130), 0.2),
    ("P.H.U. ANNA NOWAK  KRAKOW  POL", "Inne", "?", (10, 80), 0.1),
]

# BLIK e-commerce: (intermediary, shop shown in title, category, canonical, range, weekly freq)
BLIK_SHOPS = [
    ("PayU S.A. Grunwaldzka 186 Poznan", "allegro.pl", "Zakupy online (inne)", "Allegro", (20, 400), 0.5),
    ("Cashbill eComm Sobieskiego 2 Katowice", "doci.pl", "Zdrowie", "Doci", (4, 30), 0.1),
    ("PayPro SA Pastelowa 8 Poznan", "koleo.pl", "Transport", "Koleo", (25, 120), 0.15),
    ("PayU S.A. Grunwaldzka 186 Poznan", "pyszne.pl", "Restauracje i jedzenie na wynos", "Pyszne.pl", (35, 110), 0.4),
    ("Zielone Sklepy Marta Wiśniewska ul. Polna, nr 3, lok. 2 Wieliczka", "https://zielone-sklepy.pl",
     "Zakupy online (inne)", "Zielone Sklepy", (60, 300), 0.05),
]

# (day of month, counterparty, title, category, amount, foreign fx (amt, cur) or None)
SUBSCRIPTIONS = [
    (5, " NETFLIX.COM  Los Gatos  NLD ", "Subskrypcje", "Netflix", -49.00),
    (12, "Spotify P2B7C1D9E4  Stockholm  SWE", "Subskrypcje", "Spotify", -23.99),
    (18, "GOOGLE *YouTubePremium  g.co/helppay#  POL", "Subskrypcje", "YouTube Premium", -29.99),
]

FRIENDS = ["Anna Nowak", "Piotr Wiśniewski", "Katarzyna Zielińska", "Michał Wójcik", "Tomasz Lewandowski"]
BANKS = ["ALIOR Centrala", "mBank S.A.", "PKO BP S.A.", "Santander Bank Polska S.A.", "ING Bank Śląski S.A."]


def fake_nrb(rng):
    """Random Polish NRB (26 digits) with a valid IBAN checksum."""
    bban = "".join(str(rng.randint(0, 9)) for _ in range(24))
    num = int(bban + "252100")  # "PL" -> 25 21, then "00"
    return f"{98 - num % 97:02d}{bban}"


def fmt_amount(x):
    return f"{x:.2f}".replace(".", ",")


@dataclass
class Tx:
    tx_date: date
    book_date: date | None
    counterparty: str
    title: str
    category: str
    merchant: str
    kind: str
    amount: float
    account_no: str = ""
    bank: str = ""
    details: str = ""
    channel: str = "97"
    fx: tuple | None = None
    account: str = MAIN_ACCOUNT
    tx_id: str = ""  # preassigned in --overlap mode so both exports agree
    no_id: bool = False  # booked without a transaction number


def card_tx(rng, d, cp, cat, merchant, amount, fx=None):
    book = d + timedelta(days=rng.choice([0, 1, 2, 2, 3]))
    title = f"Płatność kartą {d:%d.%m.%Y} Nr karty {CARD}"
    return Tx(d, book, cp, title, cat, merchant, "card", amount,
              account_no=CARD_ACQUIRER, details="TR.KART  ", channel=rng.choice(["97", "99"]), fx=fx)


def quirk_id(quirks, book, channel):
    return f"'{book:%Y}{book.timetuple().tm_yday:03d}{channel}{quirks.randint(0, 10**9 - 1):09d}'"


def generate_quirks(quirks, d, start, end):
    """The rows of one day that came with the newer quirks, from their own random stream."""
    txs = []
    if quirks.random() < 0.7 / 7:  # coffee most weeks, at a few similar prices
        book = d + timedelta(days=quirks.choice([0, 1, 2]))
        txs.append(Tx(d, book, " COSTA COFFEE GALERIA  KRAKOW  POL ", f"Płatność kartą {d:%d.%m.%Y} Nr karty {CARD}",
                      "Restauracje i jedzenie na wynos", "Costa Coffee", "card",
                      -quirks.choice([14.50, 14.90, 15.50, 15.90]), account_no=CARD_ACQUIRER,
                      details="TR.KART  ", channel="97", tx_id=quirk_id(quirks, book, "97")))
    if quirks.random() < 0.15 / 7:  # express transfer to a friend
        friend = quirks.choice(FRIENDS)
        txs.append(Tx(d, d, f" {friend} ", quirks.choice(["za bilety", "Zwrot za zakupy", "prezent"]),
                      "Przelewy do osób", friend, "express", -float(quirks.choice([50, 80, 150, 200])),
                      account_no=f"'{fake_nrb(quirks)} '", bank=quirks.choice(BANKS), details="EXPRESS  ",
                      channel="98", tx_id=quirk_id(quirks, d, "98")))
    if d.day == 15 and d.month % 3 == 0:  # quarterly insurance premium, paid by transfer order
        txs.append(Tx(d, d, " PZU SA AL. JANA PAWŁA II 24 WARSZAWA ", f"Składka polisa {d:%m/%Y}",
                      "Rachunki i opłaty", "PZU", "transfer_order", -186.00, account_no=f"'{INSURER_NRB} '",
                      bank="PKO BP S.A.", details="PZ  ", channel="31", tx_id=quirk_id(quirks, d, "31")))
    # Once, in exports of more than three months: bank corrections without a transaction number that
    # cancel out. Early on, so the classifier tests count them among the user's first month of labels.
    if (d - start).days == 20 and (end - start).days > 90:
        for _ in range(3):
            amount = round(quirks.uniform(20, 120), 2)
            for sign, title in ((-1, "Korekta księgowania"), (1, "Storno korekty księgowania")):
                txs.append(Tx(d, d, ING_COUNTERPARTY, title, "Inne", "ING", "correction", sign * amount, no_id=True))
    return txs


def generate(rng, start, end, quirks):
    txs = []
    stores = {i: rng.randint(1000, 9999) for i in range(len(CARD_MERCHANTS))}
    d = start
    while d <= end:
        for i, (tpl, cat, merchant, (lo, hi), weekly) in enumerate(CARD_MERCHANTS):
            if rng.random() < weekly / 7:
                # Big chains: several stores, mostly the same 2-3.
                n = stores[i] + rng.choice([0, 0, 0, 7, 113])
                txs.append(card_tx(rng, d, tpl.format(n=n), cat, merchant, -round(rng.uniform(lo, hi), 2)))
        for cp, shop, cat, merchant, (lo, hi), weekly in BLIK_SHOPS:
            if rng.random() < weekly / 7:
                ref = rng.randint(10**10, 10**11 - 1)
                txs.append(Tx(d, d, cp, f"Płatność BLIK {d:%d.%m.%Y} Nr transakcji {ref} {shop}",
                              cat, merchant, "blik", -round(rng.uniform(lo, hi), 2),
                              details="TR.BLIK  ", channel="64"))
        for day, cp, cat, merchant, amount in SUBSCRIPTIONS:
            if d.day == day:
                txs.append(card_tx(rng, d, cp, cat, merchant, amount))
        if d.day == 10:
            txs.append(Tx(d, d, " ACME SOFTWARE SP. Z O.O. UL. DŁUGA 1 31-147 KRAKÓW ",
                          f"WYNAGRODZENIE ZA {(d.replace(day=1) - timedelta(days=1)):%m/%Y}",
                          "Wynagrodzenie", "ACME Software", "transfer_in", round(rng.uniform(9800, 10400), 2),
                          account_no=f"'{fake_nrb(rng)} '", bank="mBank S.A.", details="PRZELEW  ", channel="06"))
        if d.day == 11:  # own transfer: appears on both accounts
            for amount, account, other in ((-2000.00, MAIN_ACCOUNT, SAVINGS_NRB), (2000.00, SAVINGS_ACCOUNT, MAIN_NRB)):
                txs.append(Tx(d, d, OWNER, "Przelew własny - oszczędności", "Przelew własny", "Własne konto",
                              "own_transfer", amount, account_no=f"'{other} '", bank="ING Bank Śląski S.A.",
                              details="PRZELEW  ", account=account))
        if d.day == 1 and d > start:  # savings interest: booked on the last day of the previous month
            interest = round(rng.uniform(25, 60), 2)
            book = d - timedelta(days=1)
            txs.append(Tx(d, book, ING_COUNTERPARTY, "Kapitalizacja odsetek", "Odsetki", "ING", "interest",
                          interest, details="ODSETKI  ", account=SAVINGS_ACCOUNT))
            txs.append(Tx(d, book, ING_COUNTERPARTY, "Podatek od odsetek", "Odsetki", "ING", "interest_tax",
                          -round(interest * 0.19, 2), details="PODATEK  ", account=SAVINGS_ACCOUNT))
        if d.day == 1:
            txs.append(Tx(d, d, " SPÓŁDZIELNIA MIESZKANIOWA PODGÓRZE UL. WIELICKA 2 KRAKÓW ",
                          f"CZYNSZ {d:%m/%Y} LOK. 15", "Rachunki i opłaty", "Czynsz", "standing_order", -742.18,
                          account_no=f"'{RENT_NRB} '", bank="PKO BP S.A.", details="ST.ZLEC  ", channel="97"))
        if d.day == 20:
            txs.append(Tx(d, d, " TAURON SPRZEDAŻ SP. Z O.O. ", f"Faktura P/{rng.randint(10**7, 10**8)}/{d:%m}/{d:%Y}",
                          "Rachunki i opłaty", "Tauron", "transfer_out", -round(rng.uniform(110, 240), 2),
                          account_no=f"'{fake_nrb(rng)} '", bank="PKO BP S.A.", details="PRZELEW  ", channel="97"))
        if rng.random() < 0.6 / 7:  # phone transfer to a friend
            friend = rng.choice(FRIENDS)
            phone = f"+48xxxxxx{rng.randint(100, 999)}"
            txs.append(Tx(d, d, f" {friend} ",
                          f"Telephone transfer {phone} Phone transfer To {friend} From {OWNER}",
                          "Przelewy do osób", friend, "p2p_out", -float(rng.choice([20, 35, 50, 93, 120, 150])),
                          account_no=f"'{fake_nrb(rng)} '", bank=rng.choice(BANKS), details="PRZELEW  "))
        if rng.random() < 0.4 / 7:  # incoming BLIK from a friend
            friend = rng.choice(FRIENDS)
            txs.append(Tx(d, d, f" {friend} ", rng.choice([" Zakupy", "pizza", " za bilety", "Zwrot :)"]),
                          "Przelewy od osób", friend, "p2p_in", round(rng.uniform(10, 120), 2),
                          account_no=f"'{fake_nrb(rng)} '", bank=rng.choice(BANKS), details="P.BLIK  ", channel="64"))
        if rng.random() < 0.05 / 7:  # refund for a card purchase
            txs.append(card_tx(rng, d, " ZALANDO SE  Berlin  DEU ", "Zwroty", "Zalando", round(rng.uniform(80, 300), 2)))
        if rng.random() < 0.05 / 7:  # foreign-currency card payment
            eur = round(rng.uniform(5, 60), 2)
            txs.append(card_tx(rng, d, " LIDL DIENSTLEISTUNG  Berlin  DEU ", "Spożywcze", "Lidl",
                               -round(eur * 4.27, 2), fx=(-eur, "EUR")))
        txs += generate_quirks(quirks, d, start, end)
        d += timedelta(days=1)
    return txs


def finalize(txs, end):
    # Anything booked after the export date is still a pending hold.
    for t in txs:
        if t.book_date and t.book_date > end:
            t.book_date = None
            t.title = t.title.replace("Płatność kartą ", "Płatność kartą  ")  # real exports add a space
    txs.sort(key=lambda t: (t.tx_date, t.book_date or date.max))
    return txs


def to_rows(rng, txs, balances, quirks):
    balances = dict(balances)
    rows, labels = [], []
    for t in txs:
        balances[t.account] = round(balances[t.account] + t.amount, 2)
        pending = t.book_date is None
        tx_id = ""
        if t.no_id:
            pass
        elif not pending and t.tx_id:
            tx_id = t.tx_id
        elif not pending:
            tx_id = f"'{t.book_date:%Y}{t.book_date.timetuple().tm_yday:03d}{t.channel}{rng.randint(0, 10**9 - 1):09d}'"
        amt = fmt_amount(t.amount)
        fx_amt, fx_cur = (fmt_amount(t.fx[0]), t.fx[1]) if t.fx else ("", "")
        rows.append([
            t.tx_date.isoformat(), "" if pending else t.book_date.isoformat(),
            f" {t.counterparty.strip()} ", f" {t.title.strip()}",
            "" if pending else t.account_no, "" if pending else t.bank,
            "" if pending else t.details, tx_id,
            "" if pending else amt, "" if pending else "PLN",
            amt if pending else "", "PLN" if pending else "",
            fx_amt, fx_cur,
            t.account, fmt_amount(balances[t.account]), "PLN",
        ])
        labels.append([t.tx_date.isoformat(), tx_id.strip("'"), fmt_amount(t.amount), t.account,
                       t.kind, "pending" if pending else "booked", t.merchant, t.category])
    # Some same-day rows of one account are listed in a different order than their balances.
    i = 0
    while i < len(txs) - 1:
        a, b = txs[i], txs[i + 1]
        if a.tx_date == b.tx_date and a.account == b.account and quirks.random() < 0.04:
            rows[i], rows[i + 1] = rows[i + 1], rows[i]
            labels[i], labels[i + 1] = labels[i + 1], labels[i]
            i += 1
        i += 1
    rows.reverse()
    labels.reverse()
    return rows, labels


def fmt_nrb(nrb):
    return f"{nrb[:2]} " + " ".join(nrb[i:i + 4] for i in range(2, 26, 4))


def preamble(start, end, txs):
    """Lines before the header, matching the real export's layout (fake values)."""
    credits = [t.amount for t in txs if t.amount > 0]
    debits = [-t.amount for t in txs if t.amount < 0]
    return [
        '"Lista transakcji";;;;;"ING Bank Śląski S.A. ul. Sokolska 34, 40-086 Katowice www.ing.pl"' + ";" * 15,
        f'"Dokument nr {DOCUMENT_NO}_{end:%d%m%y}";',
        f'"Wygenerowany dnia: {end:%Y-%m-%d}, 12:36"' + ";" * 20,
        "",
        '"Dane Użytkownika:";',
        f'"{OWNER_PREAMBLE}";',
        "",
        '"Wybrane rachunki:";',
        f'"{MAIN_ACCOUNT} (PLN)";;"{fmt_nrb(MAIN_NRB)}";',
        f'"{SAVINGS_ACCOUNT} (PLN)";;"{fmt_nrb(SAVINGS_NRB)}";',
        "",
        '"Zastosowane kryteria wyboru";;;;;"Podsumowanie";;',
        "",
        f'"Zakres dat:";"{start:%Y-%m-%d} - {end:%Y-%m-%d}";"Typy transakcji:";"wszystkie";;;;',
        "",
        f';;;;;"Suma uznań ({len(credits)}):";{fmt_amount(sum(credits))};PLN' + ";" * 13,
        "",
        f';;;;;"Suma obciążeń ({len(debits)}):";{fmt_amount(sum(debits))};PLN' + ";" * 13,
        "",
    ]


FOOTER = ["", "", '"Dokument ma charakter informacyjny, nie stanowi dowodu księgowego";']


def format_line(row, delimiter, quoted_columns):
    row = row + [""] * EXTRA_FIELDS
    return delimiter.join(
        f'"{v.replace(chr(34), chr(34) * 2)}"' if v and i in quoted_columns else v for i, v in enumerate(row))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--months", type=int, default=6)
    p.add_argument("--end", type=date.fromisoformat, default=date(2026, 10, 28), help="export date")
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--opening-balance", type=float, default=18000.0, help="main account")
    p.add_argument("--savings-balance", type=float, default=40000.0)
    p.add_argument("--delimiter", default=";", help="';' (real export) or '\\t' (as pasted)")
    p.add_argument("--encoding", default="cp1250", help="cp1250 (real export) or utf-8")
    p.add_argument("--no-preamble", action="store_true", help="omit preamble/footer and quotes (spreadsheet paste)")
    p.add_argument("--overlap", action="store_true", help="write two overlapping exports (<out>_a, <out>_b)")
    p.add_argument("--out", default="fake_ing.csv")
    args = p.parse_args()
    if args.delimiter == "\\t":
        args.delimiter = "\t"

    rng = random.Random(args.seed)
    quirks = random.Random(args.seed + 1_000_003)
    global MAIN_NRB, SAVINGS_NRB, RENT_NRB, INSURER_NRB
    MAIN_NRB, SAVINGS_NRB, RENT_NRB = fake_nrb(rng), fake_nrb(rng), fake_nrb(rng)
    INSURER_NRB = fake_nrb(quirks)
    opening = {MAIN_ACCOUNT: args.opening_balance, SAVINGS_ACCOUNT: args.savings_balance}
    start = args.end - timedelta(days=30 * args.months)

    if args.overlap:
        write_overlapping(args, rng, quirks, start, opening)
        return

    txs = finalize(generate(rng, start, args.end, quirks), args.end)
    rows, labels = to_rows(rng, txs, opening, quirks)
    write_export(args, args.out, start, args.end, txs, rows, labels)


def write_export(args, out, start, end, txs, rows, labels):
    if args.no_preamble:
        lines = [args.delimiter.join(r) for r in [HEADER, *rows]]
    else:
        lines = preamble(start, end, txs)
        lines.append(format_line(HEADER, args.delimiter, set(range(len(HEADER)))))
        lines += [format_line(r, args.delimiter, QUOTED_COLUMNS) for r in rows]
        lines += FOOTER
    with open(out, "w", encoding=args.encoding, newline="") as f:
        f.write("\n".join(lines) + "\n")

    labels_path = out.rsplit(".", 1)[0] + ".labels.csv"
    with open(labels_path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["row", "tx_date", "tx_id", "amount", "account", "kind", "status", "merchant", "category"])
        w.writerows([[i + 1, *l] for i, l in enumerate(labels)])

    print(f"{len(rows)} rows -> {out}, labels -> {labels_path}")


def write_overlapping(args, rng, quirks, start, opening):
    """Two exports of one history: A ends 10 days before B, and B starts 14 days before A ends."""
    full = generate(rng, start, args.end, quirks)
    full.sort(key=lambda t: (t.tx_date, t.book_date))
    for t in (t for t in full if not t.tx_id and not t.no_id):  # one id per transaction, shared by both exports
        t.tx_id = f"'{t.book_date:%Y}{t.book_date.timetuple().tm_yday:03d}{t.channel}{rng.randint(0, 10**9 - 1):09d}'"
    end_a = args.end - timedelta(days=10)
    start_b = end_a - timedelta(days=14)
    stem = args.out.rsplit(".", 1)[0]
    for suffix, (lo, hi) in (("a", (start, end_a)), ("b", (start_b, args.end))):
        txs = finalize([replace(t) for t in full if lo <= t.tx_date <= hi], hi)
        balances = {a: round(b + sum(t.amount for t in full if t.account == a and t.tx_date < lo), 2)
                    for a, b in opening.items()}
        rows, labels = to_rows(rng, txs, balances, quirks)
        write_export(args, f"{stem}_{suffix}.csv", lo, hi, txs, rows, labels)


if __name__ == "__main__":
    main()
