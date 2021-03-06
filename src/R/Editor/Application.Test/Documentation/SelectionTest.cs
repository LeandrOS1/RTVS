﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Common.Core.Services;
using Microsoft.Common.Core.Shell;
using Microsoft.R.Components.ContentTypes;
using Microsoft.UnitTests.Core.XUnit;
using Xunit;

namespace Microsoft.R.Editor.Application.Test.Selection {
    [ExcludeFromCodeCoverage]
    [Collection(CollectionNames.NonParallel)]
    public class DocumentationTest {
        private readonly ICoreShell _coreShell;
        private readonly EditorHostMethodFixture _editorHost;

        public DocumentationTest(IServiceContainer services, EditorHostMethodFixture editorHost) {
            _coreShell = services.GetService<ICoreShell>();
            _editorHost = editorHost;
        }

        [Test]
        [Category.Interactive]
        public async Task InsertRoxygenBlock() {
            string content =
@"
x <- function(a,b,c) { }
";
            string expected =
@"#' Title
#'
#' @param a
#' @param b
#' @param c
#'
#' @return
#' @export
#'
#' @examples
x <- function(a,b,c) { }
";

            using (var script = await _editorHost.StartScript(_coreShell, content, RContentTypeDefinition.ContentType)) {
                script.Type("###");
                var actual = script.TextBuffer.CurrentSnapshot.GetText();
                actual.Should().Be(expected);
            }
        }
    }
}
