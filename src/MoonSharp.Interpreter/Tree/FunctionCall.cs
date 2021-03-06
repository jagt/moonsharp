﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Antlr4.Runtime.Tree;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Grammar;
using MoonSharp.Interpreter.Tree.Expressions;

namespace MoonSharp.Interpreter.Tree
{
	class FunctionCall : NodeBase
	{
		Expression[] m_Arguments;
		string m_Name;
		string m_DebugErr;

		public FunctionCall(LuaParser.NameAndArgsContext nameAndArgs, ScriptLoadingContext lcontext)
			: base(nameAndArgs, lcontext)
		{
			var name = nameAndArgs.NAME();
			m_Name = name != null ? name.GetText().Trim() : null;
			m_Arguments = nameAndArgs.args().children.SelectMany(t => NodeFactory.CreateExpressions(t, lcontext)).Where(t => t != null).ToArray();
			m_DebugErr = nameAndArgs.Parent.GetText();
		}

		public override void Compile(Execution.VM.ByteCode bc)
		{
			int argslen = m_Arguments.Length;

			if (!string.IsNullOrEmpty(m_Name))
			{
				bc.Emit_Copy(0);
				bc.Emit_Literal(DynValue.NewString(m_Name));
				bc.Emit_Index();
				bc.Emit_Swap(0, 1);
				++argslen;
			}

			for (int i = 0; i < m_Arguments.Length; i++)
				m_Arguments[i].Compile(bc);

			if (!string.IsNullOrEmpty(m_Name))
			{
				bc.Emit_ThisCall(argslen, m_DebugErr);
			}
			else
			{
				bc.Emit_Call(argslen, m_DebugErr);
			}
		}
	}
}
