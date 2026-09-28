using Bsft.EventAggregator.Saga;

namespace Bsft.Testing.Tests.Support;

public record SurveyState
{
    public bool IsExpired { get; set; }
}
