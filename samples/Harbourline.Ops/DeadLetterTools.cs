using Azure.Core;
using Azure.Messaging.ServiceBus;

namespace Harbourline.Ops;

/// <summary>Dead-letter triage and replay (Chapter 24).</summary>
public static class DeadLetterTools
{
    public static async Task PeekAsync(
        string ns, string queue, TokenCredential credential)
    {
        await using var client = new ServiceBusClient(ns, credential);
        await using var receiver = client.CreateReceiver(queue,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
            });

        var messages = await receiver.PeekMessagesAsync(100);
        foreach (var m in messages)
        {
            Console.WriteLine($"{m.MessageId,-16} {m.DeadLetterReason,-24} " +
                $"{m.EnqueuedTime:u}  {m.DeadLetterErrorDescription}");
        }

        Console.WriteLine($"{messages.Count} message(s) shown.");
    }

    // <listing:dlq-replay>
    public static async Task ReplayAsync(
        string ns, string queue, string? reason,
        TokenCredential credential, int max = 500)
    {
        await using var client = new ServiceBusClient(ns, credential);
        await using var receiver = client.CreateReceiver(queue,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,            // <1>
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
            });
        await using var sender = client.CreateSender(queue);
        var replayed = 0;

        while (replayed < max)
        {
            var batch = await receiver.ReceiveMessagesAsync(
                maxMessages: 20, maxWaitTime: TimeSpan.FromSeconds(5));
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var dead in batch)
            {
                if (reason is not null && dead.DeadLetterReason != reason)
                {
                    await receiver.AbandonMessageAsync(dead); // <2>
                    continue;
                }

                var copy = new ServiceBusMessage(dead);    // <3>
                copy.ApplicationProperties["replayedFrom"] = "dlq";
                await sender.SendMessageAsync(copy);
                await receiver.CompleteMessageAsync(dead); // <4>
                replayed++;
            }
        }

        Console.WriteLine($"Replayed {replayed} message(s).");
    }
    // </listing:dlq-replay>
}
