using System.Globalization;

namespace Bsft.Scheduling;

/// <summary>
/// One parsed field of a cron expression: the set of values, within a fixed range, that the field
/// permits. A field is built once at parse time and only ever queried afterwards, so matching costs
/// a single array lookup instead of re-interpreting the text on every candidate minute.
/// </summary>
internal sealed class CronField
{
    private readonly bool[] _allowed;
    private readonly int _min;

    private CronField(bool[] allowed, int min, bool isUnrestricted)
    {
        _allowed = allowed;
        _min = min;
        IsUnrestricted = isUnrestricted;
    }

    /// <summary>
    /// Gets a value indicating whether the field was written as <c>*</c> and therefore places no
    /// restriction at all. The day-of-month and day-of-week fields need to tell "matches everything"
    /// apart from "happens to allow every value", because their combination rule depends on it.
    /// </summary>
    public bool IsUnrestricted { get; }

    /// <summary>Determines whether the field permits the given value.</summary>
    /// <param name="value">The value to test, in the field's own range.</param>
    /// <returns><c>true</c> when the value is permitted.</returns>
    public bool Contains(int value)
    {
        var index = value - _min;
        return index >= 0 && index < _allowed.Length && _allowed[index];
    }

    /// <summary>
    /// Parses a single cron field.
    /// </summary>
    /// <param name="spec">The field text, for example <c>*/15</c> or <c>MON-FRI</c>.</param>
    /// <param name="range">The range and vocabulary the field is written in.</param>
    /// <returns>The parsed field.</returns>
    /// <exception cref="CronFormatException">Thrown when the field text is not valid.</exception>
    internal static CronField Parse(string spec, CronFieldRange range)
    {
        var allowed = new bool[range.Max - range.Min + 1];
        var isUnrestricted = string.Equals(spec, "*", StringComparison.Ordinal);
        var parser = new TermParser(range);

        foreach (var term in spec.Split(','))
        {
            if (term.Length == 0)
                throw new CronFormatException($"The {range.FieldName} field '{spec}' contains an empty term.");

            parser.Apply(term, allowed);
        }

        return new CronField(allowed, range.Min, isUnrestricted);
    }

    /// <summary>
    /// Parses the comma-separated terms of one field. It exists so that the range, the vocabulary and
    /// the field name are stated once and carried as state, rather than threaded through every
    /// parsing step as arguments.
    /// </summary>
    private sealed class TermParser
    {
        private readonly CronFieldRange _range;

        public TermParser(CronFieldRange range) => _range = range;

        /// <summary>Marks every value a single term selects as allowed.</summary>
        /// <param name="term">The term text, for example <c>MON-FRI</c> or <c>*/15</c>.</param>
        /// <param name="allowed">The field's value map, indexed from the range minimum.</param>
        public void Apply(string term, bool[] allowed)
        {
            var (rangeSpec, step, hasStep) = SplitStep(term);
            var (first, last) = ParseRange(rangeSpec, hasStep);

            for (var value = first; value <= last; value += step)
            {
                allowed[value - _range.Min] = true;
            }
        }

        private (string RangeSpec, int Step, bool HasStep) SplitStep(string term)
        {
            var slash = term.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
                return (term, 1, false);

            var stepText = term[(slash + 1)..];

            // The upper bound is not cosmetic. A step is added to the running value, so an unbounded
            // one overflows int and wraps to a large negative number that still passes the loop's
            // "value <= last" test — which would index the value map out of bounds and throw an
            // exception this parser promises never to raise. A step wider than the field itself
            // selects nothing beyond its first value anyway, so rejecting it loses nothing.
            var span = _range.Max - _range.Min;
            if (!int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out var step)
                || step < 1
                || step > span)
            {
                throw new CronFormatException(
                    $"The {_range.FieldName} field has an invalid step '{stepText}'; a step must be between 1 and {span}.");
            }

            return (term[..slash], step, true);
        }

        private (int First, int Last) ParseRange(string rangeSpec, bool hasStep)
        {
            if (string.Equals(rangeSpec, "*", StringComparison.Ordinal))
                return (_range.Min, _range.Max);

            // A term starting with the step separator, "/2", leaves nothing in front of it. An empty
            // term between two commas is already refused before a term ever gets here, so this is the
            // one remaining way to arrive with nothing to parse — and the hyphen search below starts
            // at index one, which an empty string does not have. Rejecting it keeps the promise this
            // parser makes to TryParse: a malformed expression leaves through CronFormatException and
            // nothing else, so validating a configured schedule reports it instead of crashing.
            if (rangeSpec.Length == 0)
                throw new CronFormatException($"The {_range.FieldName} field has a step with no value in front of it.");

            // A hyphen at position zero would be a negative number rather than a range separator, and
            // no cron field accepts one; searching from index one keeps the error message accurate.
            var hyphen = rangeSpec.IndexOf('-', 1);
            if (hyphen < 0)
            {
                var single = ParseValue(rangeSpec);

                // "5/15" is the widely used extension meaning "from 5 to the end of the range in steps
                // of 15", so a step turns a single value into an open-ended range. Without one, "5" is
                // just 5 — widening it there would silently fire the job every unit after the fifth.
                return hasStep ? (single, _range.Max) : (single, single);
            }

            var first = ParseValue(rangeSpec[..hyphen]);
            var last = ParseValue(rangeSpec[(hyphen + 1)..]);

            if (last < first)
                throw new CronFormatException($"The {_range.FieldName} field has a descending range '{rangeSpec}'.");

            return (first, last);
        }

        private int ParseValue(string text)
        {
            if (_range.Names is not null && _range.Names.TryGetValue(text, out var named))
                return named;

            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new CronFormatException($"The {_range.FieldName} field has an unrecognised value '{text}'.");

            if (value < _range.Min || value > _range.Max)
                throw new CronFormatException($"The {_range.FieldName} field value '{text}' is outside the range {_range.Min}-{_range.Max}.");

            return value;
        }
    }
}
