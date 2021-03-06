﻿using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Application;
using SharedApplication;

namespace Textrude
{
    /// <summary>
    ///     Renders a template based on the supplied parameters
    /// </summary>
    public class CmdRender
    {
        private readonly RenderOptions _options;
        private readonly RunTimeEnvironment _runtime;
        private readonly Helpers _sys;

        public CmdRender(RenderOptions options, RunTimeEnvironment rte, Helpers sys)
        {
            _options = options;
            _runtime = rte;
            _sys = sys;
        }

        private DateTime[] GetLastWrittenDates(IEnumerable<string> files, DateTime fallback, bool allowNotExist)
        {
            DateTime GetLastWriteTime(string file)
            {
                if (allowNotExist && !_runtime.FileSystem.Exists(file))
                    return fallback;

                return _runtime.FileSystem.GetLastWriteTimeUtc(file);
            }

            var f = files
                .Select(f =>
                    _sys.GetOrQuit(() => GetLastWriteTime(f), $"Missing/inaccessible file '{f}'")
                )
                .ToArray();
            //ensure we always have at least one date in the set
            if (!f.Any())
                f = f.Append(fallback).ToArray();
            return f;
        }

        private string TryReadFile(string path)
        {
            return _sys.GetOrQuit(() => _runtime.FileSystem.ReadAllText(path),
                $"Unable to read file {_options.Template}");
        }


        public void Run()
        {
            var models = NamedFileFactory.ToNamedFiles(_options.Models, NameProvider.IndexedModel);
            var outputs = NamedFileFactory.ToNamedFiles(_options.Output, NameProvider.IndexedOutput);

            var lastModelDate = GetLastWrittenDates(models.Select(m => m.Path), DateTime.MinValue, false).Max();
            var earliestOutputDate = GetLastWrittenDates(outputs.Select(m => m.Path), DateTime.MinValue, true).Min();
            var templateDate = GetLastWrittenDates(new[] {_options.Template}, DateTime.MinValue, false).Max();

            var lastInputDate = lastModelDate > templateDate ? lastModelDate : templateDate;


            //check if there is nothing to do...
            if (_options.Lazy && (lastInputDate < earliestOutputDate))
                return;


            var template = TryReadFile(_options.Template);

            var engine = new ApplicationEngine(_runtime)
                .WithHelpers()
                .WithDefinitions(_options.Definitions)
                .WithTemplate(template);

            foreach (var model in models)
            {
                var modelText = TryReadFile(model.Path);
                var format = ModelDeserializerFactory.FormatFromExtension(model.Path);
                engine = engine.WithModel(model.Name, modelText, format);
            }

            engine = engine.WithIncludePaths(_options.Include);

            if (!_options.HideEnvironment)
                engine = engine.WithEnvironmentVariables();

            engine.Render();

            foreach (var engineError in engine.Errors)
            {
                Console.Error.WriteLine(engineError);
            }

            if (engine.HasErrors)
                _sys.GetOrQuit<int>(() => throw new ApplicationException(), "");


            if (outputs.Any())
            {
                foreach (var output in outputs)
                {
                    var text = engine.GetOutputFromVariable(output.Name);
                    _sys.TryOrQuit(() => _runtime.FileSystem.WriteAllText(output.Path, text),
                        $"Unable to write output to {output.Path}");
                }
            }
            else
            {
                var outputStrings = engine.GetOutput(10);
                for (var i = 0; i < outputs.Length; i++)
                {
                    var text = outputStrings[i];
                    if (text.Length == 0)
                        continue;
                    Console.WriteLine($"----------------- output{i} -------------------");
                    Console.WriteLine(text);
                }
            }
        }

        public static void Run(RenderOptions options, RunTimeEnvironment rte, Helpers sys)
        {
            var cmd = new CmdRender(options, rte, sys);
            cmd.Run();
        }
    }
}
