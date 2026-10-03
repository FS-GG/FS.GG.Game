// Probe malformed values directly at the generated typed API boundary.
const { invalidLimitProbe } = await import(process.argv[2]);
for (const value of [NaN, Infinity, -Infinity, 1.5, 0, -1, 2147483648, '10', true, null, undefined])
  invalidLimitProbe({ MaximumDeadlineMilliseconds: value, MaximumOutputBytes: 44, EnclosingDeadlineMilliseconds: undefined });
for (const value of [NaN, Infinity, 1.5, 0, -1, 65537, '44', true, null, undefined])
  invalidLimitProbe({ MaximumDeadlineMilliseconds: 10, MaximumOutputBytes: value, EnclosingDeadlineMilliseconds: undefined });
console.log('raw JavaScript malformed deadline/output controls: PASS');
