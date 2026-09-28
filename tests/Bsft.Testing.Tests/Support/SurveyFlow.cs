using Bsft.EventAggregator.Saga;

namespace Bsft.Testing.Tests.Support;

/// <summary>A second flow, so a drain has to work across more than one saga instance.</summary>
public class SurveyFlow : ISagaFlow
{
    public void Configure(ISagaBuilderFactory factory)
    {
        factory.WithState<SurveyState>()
            .StartWith<SurveySent>(e => e.SurveyId)
            .Then((ctx, e) =>
            {
                ctx.ScheduleTimeout("expiry", TimeSpan.FromDays(1), new SurveyExpired(e.SurveyId));
                return Task.CompletedTask;
            })
            .WaitFor<SurveyExpired>(e => e.SurveyId)
            .Then((ctx, _) =>
            {
                ctx.State.IsExpired = true;
                return Task.CompletedTask;
            })
            .Build();
    }
}
