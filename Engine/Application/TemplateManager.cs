﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Engine.Application
{
    /// <summary>
    ///     Wraps the Scriban template engine to make it easier to use
    /// </summary>
    public class TemplateManager
    {
        /// <summary>
        ///     underlying template context
        /// </summary>
        private readonly TemplateContext _context = new();

        /// <summary>
        ///     loader for included scripts
        /// </summary>
        private readonly ScriptLoader _scriptLoader;

        /// <summary>
        ///     The top-most scriptObject in the context
        /// </summary>
        private readonly ScriptObject _top = new();

        /// <summary>
        ///     The template after compilation
        /// </summary>
        private Template _compiledTemplate;

        /// <summary>
        ///     Output after render pass
        /// </summary>
        private string _output = string.Empty;

        public TemplateManager(IFileSystemOperations ops)
        {
            _context.StrictVariables = true;
            _scriptLoader = new ScriptLoader(ops);
            _context.TemplateLoader = _scriptLoader;
            _context.PushGlobal(_top);
        }

        /// <summary>
        ///     Cumulative list of errors from template operations
        /// </summary>
        public ImmutableArray<string> ErrorList { get; private set; } = ImmutableArray<string>.Empty;

        /// <summary>
        ///     Run the render pass and capture any errors
        /// </summary>
        public string Render()
        {
            if (ErrorList.Any())
                return string.Empty;


            _output = _compiledTemplate.Render(_context);
            if (_compiledTemplate.HasErrors)
                ErrorList = ErrorList
                    .Concat(_compiledTemplate.Messages.Select(e => $"ERROR: {e}"))
                    .ToImmutableArray();

            return _output;
        }

        /// <summary>
        ///     Compile a template
        /// </summary>
        /// <remarks>
        ///     This should not be able to throw an exception but we want to ensure that we catch any Scriban bugs
        /// </remarks>
        public void SetTemplate(string templateText)
        {
            try
            {
                _compiledTemplate = Template.Parse(templateText);

                ErrorList = _compiledTemplate
                    .Messages
                    .Select(e => $"ERROR: {e}")
                    .ToImmutableArray();
            }
            catch (Exception e)
            {
                ErrorList = ErrorList.Add($"SCRIBAN INTERNAL EXCEPTION: {e}");
            }
        }

        /// <summary>
        ///     Adds a variable to the context
        /// </summary>
        public void AddVariable(string name, object val)
        {
            _top.Add(name, val);
        }

        /// <summary>
        ///     Adds an include path to the ScriptLoader used by the template engine
        /// </summary>
        public void AddIncludePath(string path)
        {
            _scriptLoader.AddIncludePath(path);
        }

        /// <summary>
        ///     Attempts to read the value of a global variable from the engine
        /// </summary>
        /// <remarks>
        ///     If the variable doesn't exist, we return the empty string
        /// </remarks>
        public string TryGetVariable(string variableName)
        {
            var scribanVariable = new ScriptVariableGlobal(variableName);
            try
            {
                return _context.GetValue(scribanVariable)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }


        public static ImmutableArray<ModelPath> PathsForObjectTree(IDictionary<string, object> container,
            ModelPath prefix)
        {
            var ret = new List<ModelPath>();


            foreach (var keyValuePair in container)
            {
                var type = keyValuePair.Value.GetType().FullName;

                var mType = ModelPath.PathType.Property;
                if (type.Contains("Scriban") && type.Contains("Function"))
                    mType = ModelPath.PathType.Method;


                var p = prefix.WithChild(keyValuePair.Key).WithType(mType);
                if (keyValuePair.Value is IDictionary<string, object> child)
                    ret.AddRange(PathsForObjectTree(child, p));
                else ret.Add(p);
            }

            return ret.ToImmutableArray();
        }

        public ImmutableArray<ModelPath> GetBuiltIns() => PathsForObjectTree(_context.BuiltinObject, ModelPath.Empty);
        public ImmutableArray<ModelPath> GetObjectTree() => PathsForObjectTree(_top, ModelPath.Empty);

        public ImmutableArray<ModelPath> GetKeywords()
        {
            var keywords =
                @"func end if else for break continue
                  in  while capture readonly import
                  with wrap include ret case  when this
                empty tablerow";
            return keywords.Split(" \r\n".ToCharArray(),
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => ModelPath.FromString(s).WithType(ModelPath.PathType.Keyword))
                .ToImmutableArray();
        }

        public ImmutableArray<ModelPath> ModelPaths() =>
            GetBuiltIns()
                .Concat(GetObjectTree())
                .Concat(GetKeywords())
                .ToImmutableArray();
    }
}
