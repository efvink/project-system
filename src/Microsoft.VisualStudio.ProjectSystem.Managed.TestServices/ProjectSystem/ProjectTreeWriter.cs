﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.ProjectSystem;

namespace Microsoft.VisualStudio.ProjectSystem
{
    internal class ProjectTreeWriter
    {
        private readonly StringBuilder _builder  = new StringBuilder();
        private readonly ProjectTreeWriterOptions _options;
        private readonly IProjectTree _parent;

        public ProjectTreeWriter(IProjectTree tree, ProjectTreeWriterOptions options)
        {
            Requires.NotNull(tree, nameof(tree));

            _parent = tree;
            _options = options;
        }

        private bool TagElements
        {
            get { return (_options & ProjectTreeWriterOptions.Tags) == ProjectTreeWriterOptions.Tags; }
        }

        public static string WriteToString(IProjectTree tree)
        {
            ProjectTreeWriter writer = new ProjectTreeWriter(tree, ProjectTreeWriterOptions.AllProperties);
            return writer.WriteToString();
        }

        public string WriteToString()
        {
            _builder.Clear();

            WriteProjectItem(_parent);

            return _builder.ToString();
        }

        private void WriteProjectItem(IProjectTree tree, int indentLevel = 0)
        {
            WriteIndentLevel(indentLevel);
            WriteCaption(tree);
            WriteProperties(tree);
            WriteFilePath(tree);
            WriteIcons(tree);
            WriteChildren(tree, indentLevel);
        }

        private void WriteChildren(IProjectTree tree, int indentLevel)
        {
            foreach (IProjectTree child in tree.Children)
            {
                _builder.AppendLine();
                WriteProjectItem(child, indentLevel + 1);
            }
        }

        private void WriteIndentLevel(int indentLevel)
        {
            if (TagElements)
            {
                for (int i = 0; i < indentLevel; i++)
                {
                    _builder.Append("[indent]");
                }
            }
            else
            {
                _builder.Append(' ', indentLevel * 4);
            }
        }

        private void WriteCaption(IProjectTree tree)
        {
            _builder.Append(tree.Caption);

            if (TagElements)
            {
                _builder.Append("[caption]");
            }
        }

        private void WriteProperties(IProjectTree tree)
        {
            bool visibility = _options.HasFlag(ProjectTreeWriterOptions.Visibility);
            bool flags = _options.HasFlag(ProjectTreeWriterOptions.Visibility);

            if (!visibility && !flags)
                return;

            _builder.Append(' ');
            _builder.Append('(');

            if (visibility)
            {
                WriteVisibility(tree);
                _builder.Append(", ");
            }

            if (flags)
                WriteFlags(tree);

            _builder.Append(')');
        }

        private void WriteFilePath(IProjectTree tree)
        {
            if (!_options.HasFlag(ProjectTreeWriterOptions.FilePath))
                return;

            _builder.Append(", FilePath: ");
            _builder.Append('"');
            _builder.Append(tree.FilePath);
            
            if (TagElements)
            {
                _builder.Append("[filepath]");
            }

            _builder.Append('"');
        }

        private void WriteVisibility(IProjectTree tree)
        {
            _builder.Append("visibility: ");
            
            if (tree.Visible)
            {
                _builder.Append("visible");
            }
            else
            {
                _builder.Append("invisible");
            }
        }

        private void WriteFlags(IProjectTree tree)
        {
            _builder.Append("flags: ");
            _builder.Append('{');

            bool writtenCapability = false;

            foreach (string capability in tree.Flags.OrderBy(c => c, StringComparer.InvariantCultureIgnoreCase))
            {
                if (writtenCapability)
                    _builder.Append(" ");

                writtenCapability = true;
                _builder.Append(capability);

                if (TagElements)
                {
                    _builder.Append("[capability]");
                }
            }

            _builder.Append('}');
        }

        private void WriteIcons(IProjectTree tree)
        {
            if (!_options.HasFlag(ProjectTreeWriterOptions.Icons))
                return;

            WriteIcon("Icon", tree.Icon);
            WriteIcon("ExpandedIcon", tree.ExpandedIcon);
        }

        private void WriteIcon(string name, ProjectImageMoniker icon)
        {
            _builder.AppendFormat(", {0}: {{", name);

            if (icon != null)
            {
                _builder.AppendFormat("{1} {2}", name, icon.Guid.ToString("D").ToUpperInvariant(), icon.Id);
            }

            if (TagElements)
            {
                _builder.Append("[icon]");
            }

            _builder.Append("}");
        }
    }
}