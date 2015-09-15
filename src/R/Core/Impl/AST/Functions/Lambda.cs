﻿using Microsoft.R.Core.AST.DataTypes;
using Microsoft.R.Core.AST.Definitions;
using Microsoft.R.Core.AST.Scopes;

namespace Microsoft.R.Core.AST.Functions
{
    /// <summary>
    /// Represents anonymous function call as in
    /// tryCatch({ code }, ...)
    /// </summary>
    public sealed class Lambda : Scope, IRValueNode
    {
        #region IRValueNode
        public RObject GetValue()
        {
            // Evaluation should return result
            // of the last statement
            return new RFunction(this);
        }
        #endregion
    }
}