using System.Text;
using BenchmarkDotNet.Attributes;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Services;
using SaucyBot.Site.Response;

namespace SaucyBot.Tests.Benchmark.Benchmarks;

[MemoryDiagnoser]
[MinInvokeCount(3), InvocationCount(16)]
[MinWarmupCount(3), MaxWarmupCount(5)]
[MinIterationCount(3), MaxIterationCount(5)]
public class MessageManagerBenchmarks
{
    private MessageManager _messageManager = null!;
    private ProcessResponse _singleEmbedResponse = null!;
    private ProcessResponse _multiEmbedResponse = null!;
    private ProcessResponse _filesResponse = null!;
    private ProcessResponse _mixedResponse = null!;
    private ProcessResponse _textOnlyResponse = null!;

    [GlobalSetup]
    public void Setup()
    {
        var logger = Substitute.For<ILogger<MessageManager>>();
        var config = new ConfigurationBuilder().Build();
        _messageManager = new MessageManager(logger, config);

        var embed = new EmbedBuilder { Title = "Test Embed", Description = "Test Description" }.Build();

        _textOnlyResponse = new ProcessResponse(text: "This is a text-only response");

        _singleEmbedResponse = new ProcessResponse(embeds: [embed]);

        _multiEmbedResponse = new ProcessResponse(
            embeds: Enumerable.Range(0, 10).Select(i =>
                new EmbedBuilder { Title = $"Embed {i}", Description = $"Description {i}" }.Build()
            ).ToList()
        );

        var fileStream = new MemoryStream(Encoding.UTF8.GetBytes("test file content"));
        var fileAttachment = new FileAttachment(fileStream, "test.txt");
        _filesResponse = new ProcessResponse(files: [fileAttachment]);

        var multiFiles = Enumerable.Range(0, 20).Select(i =>
        {
            var content = Encoding.UTF8.GetBytes($"file content {i}");
            var fs = new MemoryStream(content);
            return new FileAttachment(fs, $"file_{i}.txt");
        }).ToList();
        _mixedResponse = new ProcessResponse(
            embeds: [embed, embed],
            files: multiFiles,
            text: "Check out these files"
        );
    }

    [Benchmark]
    public Task<List<Message>> PartitionMessages_TextOnly()
    {
        return MessageManager.PartitionMessages(_textOnlyResponse);
    }

    [Benchmark]
    public Task<List<Message>> PartitionMessages_SingleEmbed()
    {
        return MessageManager.PartitionMessages(_singleEmbedResponse);
    }

    [Benchmark]
    public Task<List<Message>> PartitionMessages_MultipleEmbeds()
    {
        return MessageManager.PartitionMessages(_multiEmbedResponse);
    }

    [Benchmark]
    public Task<List<Message>> PartitionMessages_SingleFile()
    {
        return MessageManager.PartitionMessages(_filesResponse);
    }

    [Benchmark]
    public Task<List<Message>> PartitionMessages_Mixed()
    {
        return MessageManager.PartitionMessages(_mixedResponse);
    }
}
