﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using FluentValidation;
using Sewer56.Update.Extractors.SevenZipSharp;
using Sewer56.Update.Packaging;
using Sewer56.Update.Packaging.Compressors;
using Sewer56.Update.Packaging.Interfaces;
using Sewer56.Update.Packaging.Structures;
using Sewer56.Update.Packaging.Structures.ReleaseBuilder;
using Sewer56.Update.Resolvers.NuGet;
using Sewer56.Update.Tool.Options;
using Sewer56.Update.Tool.Options.Groups;
using Sewer56.Update.Tool.Validation;

namespace Sewer56.Update.Tool;

internal class Program
{
    static async Task Main(string[] args)
    {
        var parser = new Parser(with =>
        {
            with.AutoHelp = true;
            with.CaseSensitive = false;
            with.CaseInsensitiveEnumValues = true;
            with.EnableDashDash = true;
            with.HelpWriter = null;
        });

        var parserResult = parser.ParseArguments<CreateReleaseOptions, CreateCopyPackageOptions, CreateDeltaPackageOptions>(args);
        await parserResult.WithParsedAsync<CreateReleaseOptions>(CreateRelease);
        await parserResult.WithParsedAsync<CreateCopyPackageOptions>(CreateCopyPackage);
        await parserResult.WithParsedAsync<CreateDeltaPackageOptions>(CreateDeltaPackage);
        parserResult.WithNotParsed(errs => HandleParseError(parserResult, errs));
    }

    private static async Task CreateDeltaPackage(CreateDeltaPackageOptions options)
    {
        var validator = new CreateDeltaPackageValidator();
        validator.ValidateAndThrow(options);

        var ignoreRegexes = string.IsNullOrEmpty(options.IgnoreRegexesPath) ? null : (await File.ReadAllLinesAsync(options.IgnoreRegexesPath)).ToList();
        var includeRegexes = string.IsNullOrEmpty(options.IncludeRegexesPath) ? null : (await File.ReadAllLinesAsync(options.IncludeRegexesPath)).ToList();
        await Package<Empty>.CreateDeltaAsync(options.LastVersionFolderPath, options.FolderPath, options.OutputPath, options.LastVersion, options.Version, null, ignoreRegexes, null, includeRegexes);
    }

    private static async Task CreateCopyPackage(CreateCopyPackageOptions options)
    {
        var validator = new CreateCopyPackageOptionsValidator();
        validator.ValidateAndThrow(options);

        var ignoreRegexes = string.IsNullOrEmpty(options.IgnoreRegexesPath) ? null : (await File.ReadAllLinesAsync(options.IgnoreRegexesPath)).ToList();
        var includeRegexes = string.IsNullOrEmpty(options.IncludeRegexesPath) ? null : (await File.ReadAllLinesAsync(options.IncludeRegexesPath)).ToList();
        await Package<Empty>.CreateAsync(options.FolderPath, options.OutputPath, options.Version, null, ignoreRegexes, includeRegexes);
    }

    /// <summary>
    /// Creates a new package.
    /// </summary>
    private static async Task CreateRelease(CreateReleaseOptions releaseOptions)
    {
        // Validate and set defaults.
        var validator = new CreateReleaseOptionsValidator();
        validator.ValidateAndThrow(releaseOptions);
        if (releaseOptions.MaxParallelism == CreateReleaseOptions.DefaultInt)
            releaseOptions.MaxParallelism = Environment.ProcessorCount;

        // Get in there!
        var existingPackages = string.IsNullOrEmpty(releaseOptions.ExistingPackagesPath) ? new List<string>() : (await File.ReadAllLinesAsync(releaseOptions.ExistingPackagesPath)).ToList();
        Directory.CreateDirectory(releaseOptions.OutputPath);

        // Arrange
        var builder = new ReleaseBuilder<Empty>();
        foreach (var existingPackage in existingPackages)
        {
            builder.AddExistingPackage(new ExistingPackageBuilderItem()
            {
                Path = existingPackage
            });
        }

        // Act
        using var progressBar = new ShellProgressBar.ProgressBar(10000, "Building Release");

        await builder.BuildAsync(new BuildArgs()
        {
            FileName = releaseOptions.PackageName,
            OutputFolder = releaseOptions.OutputPath,
            PackageArchiver = GetArchiver(releaseOptions),
            MaxParallelism = releaseOptions.MaxParallelism
        }, progressBar.AsProgress<double>());
    }

    private static IPackageArchiver GetArchiver(CreateReleaseOptions releaseOptions)
    {
        return releaseOptions.Archiver switch
        {
            Archiver.Zip => new ZipPackageArchiver(),
            Archiver.NuGet => new NuGetPackageArchiver(releaseOptions.GetArchiver()),
            Archiver.SharpCompress => releaseOptions.SharpCompressFormat.GetArchiver(),
            Archiver.SevenZipSharp => new SevenZipSharpArchiver(new SevenZipSharpArchiverSettings()
            {
                CompressionLevel = releaseOptions.SevenZipSharpCompressionLevel,
                ArchiveFormat = releaseOptions.SevenZipSharpArchiveFormat,
                CompressionMethod = releaseOptions.SevenZipSharpCompressionMethod
            }),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Errors or --help or --version.
    /// </summary>
    static void HandleParseError(ParserResult<object> options, IEnumerable<Error> errs)
    {
        var helpText = HelpText.AutoBuild(options, help =>
        {
            help.Copyright = "Created by Sewer56, licensed under GNU LGPL V3";
            help.AutoHelp = false;
            help.AutoVersion = false;
            help.AddDashesToOption = true;
            help.AddEnumValuesToHelpText = true;
            help.AddNewLineBetweenHelpSections = true;
            help.AdditionalNewLineAfterOption = true;
            return HelpText.DefaultParsingErrorsHandler(options, help);
        }, example => example, true);

        Console.WriteLine(helpText);
    }
}