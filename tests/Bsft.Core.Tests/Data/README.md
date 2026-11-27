# Test data

- `IdnaTestV2.txt`: the official UTS 46 conformance vectors, Unicode 18.0.0, unchanged, fetched from
  <https://www.unicode.org/Public/18.0.0/idna/IdnaTestV2.txt>. `IdnaConformanceTests` runs every case against
  `InternetDomain.TryCanonicalizeDomain` (non-transitional processing, every check on). Move it together with the pinned
  Unicode version in `tools/unicode-data/generate.py`.
- `normalization-vectors.txt`: a selection of `NormalizationTest.txt` 18.0.0 for `UnicodeNormalizationTests`: part 0, 2, 3,
  4 and 5 whole, part 1 without its identity lines and with every sixth of the others.
