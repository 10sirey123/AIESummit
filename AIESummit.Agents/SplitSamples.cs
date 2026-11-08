using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;

namespace AIESummit.Agents;

public sealed class SplitSamples : IUniqueTagSampleProvider
{
    public const string ModelTag = "foundry-split";

    public IEnumerable<IHaveUniqueTag> ProvideSamples()
    {
        yield return new LLMModel { Tag = ModelTag, Name = "gemini-3.8-flash", Provider = LLMAPIProvider.OpenAI };

        var step = SplitStep.Definition();
        step.Model = TaggedReference.To<LLMModel>(ModelTag);
        yield return step;
    }
}
