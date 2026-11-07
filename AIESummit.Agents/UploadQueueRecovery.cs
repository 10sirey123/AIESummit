using Blueshift.Agents.Abstract;
using Blueshift.Agents.Infrastructure.Queue;
using Blueshift.Agents.LLM;
using AIESummit.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIESummit.Agents;

// The development Service Bus emulator recreates its queues when it restarts.
public sealed class UploadQueueRecovery(IQueueManager queues, ILogger<UploadQueueRecovery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(45));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var agent in RootBaseAgent.AgentInfos)
            {
                if (agent.Instance is not (OcrAgent or LLMStepAgent<UploadedFile> or LLMStepAgent<OcrPage> or LLMStepAgent<DocumentAnalysis> or LlmPromptAgent) || !agent.Enabled)
                    continue;
                try
                {
                    await agent.Instance.EnqueuePendingObjects(queues);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Queue recovery for {Agent} failed ({ErrorType}).",
                        agent.AgentType.Name, ex.GetBaseException().GetType().Name);
                }
            }
        }
    }
}
