﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MoonSharp.Interpreter.Execution;

namespace MoonSharp.Interpreter.Interop
{
	internal static class ConversionHelper
	{
		static readonly HashSet<Type> NumericTypes = new HashSet<Type>()
		{
			typeof(sbyte), 
			typeof(byte), 
			typeof(short), 
			typeof(ushort), 
			typeof(int), 
			typeof(uint), 
			typeof(long), 
			typeof(ulong), 
			typeof(float), 
			typeof(decimal), 
			typeof(double)
		};


		internal static bool CheckCallbackSignature(MethodInfo mi)
		{
			ParameterInfo[] pi = mi.GetParameters();

			return (pi.Length == 2 && pi[0].ParameterType == typeof(ScriptExecutionContext)
				&& pi[1].ParameterType == typeof(CallbackArguments) && mi.ReturnType == typeof(DynValue));
		}

		internal static DynValue TryClrObjectToSimpleMoonSharpValue(Script script, object obj)
		{
			if (obj == null)
				return DynValue.Nil;

			if (obj is DynValue)
				return (DynValue)obj;

			Type t = obj.GetType();

			if (obj is bool)
				return DynValue.NewBoolean((bool)obj);

			if (obj is string || obj is StringBuilder || obj is char)
				return DynValue.NewString(obj.ToString());

			if (obj is Closure)
				return DynValue.NewClosure((Closure)obj);

			if (NumericTypes.Contains(t))
				return DynValue.NewNumber(TypeToDouble(t, obj));

			if (obj is Table)
				return DynValue.NewTable((Table)obj);

			if (obj is CallbackFunction)
				return DynValue.NewCallback((CallbackFunction)obj);

			if (obj is Delegate)
			{
				Delegate d = (Delegate)obj;
				MethodInfo mi = d.Method;

				if (CheckCallbackSignature(mi))
					return DynValue.NewCallback((Func<ScriptExecutionContext, CallbackArguments, DynValue>)d);
			}

			return null;
		}


		internal static DynValue ClrObjectToComplexMoonSharpValue(Script script, object obj)
		{
			DynValue v = TryClrObjectToSimpleMoonSharpValue(script, obj);

			if (v != null) return v;

			v = UserData.Create(obj);

			if (v != null) return v;

			if (obj is Type)
				v = UserData.CreateStatic(obj as Type);

			if (v != null) return v;

			if (obj is Delegate)
				return DynValue.NewCallback(CallbackFunction.FromDelegate(script, (Delegate)obj));

			if (obj is MethodInfo)
			{
				MethodInfo mi = (MethodInfo)obj;
				
				if (mi.IsStatic)
				{
					return DynValue.NewCallback(CallbackFunction.FromMethodInfo(script, mi));
				}
			}


			if (obj is System.Collections.IList)
			{
				Table t = ConvertIListToTable(script, (System.Collections.IList)obj);
				return DynValue.NewTable(t);
			}

			if (obj is System.Collections.IDictionary)
			{
				Table t = ConvertIDictionaryToTable(script, (System.Collections.IDictionary)obj);
				return DynValue.NewTable(t);
			}

			if (obj is System.Collections.IEnumerable)
			{
				var enumer = (System.Collections.IEnumerable)obj;
				return EnumerableWrapper.ConvertIterator(script, enumer.GetEnumerator());
			}

			if (obj is System.Collections.IEnumerator)
			{
				var enumer = (System.Collections.IEnumerator)obj;
				return EnumerableWrapper.ConvertIterator(script, enumer);
			}

			throw ScriptRuntimeException.ConvertObjectFailed(obj);
		}

		private static Table ConvertIDictionaryToTable(Script script, System.Collections.IDictionary dict)
		{
			Table t = new Table(script);
			
			foreach(System.Collections.DictionaryEntry kvp in dict)
			{
				DynValue key = ClrObjectToComplexMoonSharpValue(script, kvp.Key);
				DynValue val = ClrObjectToComplexMoonSharpValue(script, kvp.Value);
				t.Set(key, val);
			}

			return t;
		}

		private static Table ConvertIListToTable(Script script, System.Collections.IList list)
		{
			Table t = new Table(script);
			for (int i = 0; i < list.Count; i++)
			{
				t[i + 1] = ClrObjectToComplexMoonSharpValue(script, list[i]);
			}
			return t;
		}

		internal static object MoonSharpValueToClrObject(DynValue value)
		{
			switch (value.Type)
			{
				case DataType.Void:
				case DataType.Nil:
					return null;
				case DataType.Boolean:
					return value.Boolean;
				case DataType.Number:
					return value.Number;
				case DataType.String:
					return value.String;
				case DataType.Function:
					return value.Function;
				case DataType.Table:
					return value.Table;
				case DataType.Tuple:
					return value.Tuple;
				case DataType.UserData:
					if (value.UserData.Object != null)
						return value.UserData.Object;
					else if (value.UserData.Descriptor != null)
						return value.UserData.Descriptor.Type;
					else
						return null;
				case DataType.ClrFunction:
					return value.Callback;
				default:
					throw ScriptRuntimeException.ConvertObjectFailed(value.Type);
			}
		}

		internal static object MoonSharpValueToObjectOfType(DynValue value, Type desiredType, object defaultValue)
		{
			if (desiredType == typeof(DynValue))
				return value;

			if (desiredType == typeof(object))
				return value.ToObject();

			bool isString = false;
			bool isStringBuilder = false;
			bool isChar = false;

			if (desiredType == typeof(string))
				isString = true;
			else if (desiredType == typeof(StringBuilder))
				isStringBuilder = true;
			else if (desiredType == typeof(char) && value.String.Length > 0)
				isChar = true;

			bool isAnyString = isString || isStringBuilder || isChar;
			string str = null;

			Type nt = Nullable.GetUnderlyingType(desiredType);
			Type nullableType = null;

			if (nt != null)
			{
				nullableType = desiredType;
				desiredType = nt;
			}

			switch (value.Type)
			{
				case DataType.Void:
					if (desiredType.IsValueType)
					{
						if (defaultValue != null)
							return defaultValue;

						if (nullableType != null)
							return null;
					}
					else
					{
						return defaultValue;
					}
					break;
				case DataType.Nil:
					if (desiredType.IsValueType)
					{
						if (nullableType != null)
							return null;

						if (defaultValue != null)
							return defaultValue;
					}
					else
					{
						return null;
					}
					break;
				case DataType.Boolean:
					if (desiredType == typeof(bool))
						return value.Boolean;
					if (isAnyString)
						str = value.Boolean.ToString();
					break;
				case DataType.Number:
					if (NumericTypes.Contains(desiredType))
						return DoubleToType(desiredType, value.Number);
					if (isAnyString)
						str = value.Number.ToString();
					break;
				case DataType.String:
					if (isAnyString)
						str = value.String;
					break;
				case DataType.Function:
					if (desiredType == typeof(Closure)) return value.Function;
					break;
				case DataType.ClrFunction:
					if (desiredType == typeof(CallbackFunction)) return value.Callback;
					break;
				case DataType.UserData:
					if (value.UserData.Object != null)
					{
						if (desiredType.IsInstanceOfType(value.UserData.Object))
							return value.UserData.Object;
						if (isAnyString)
							str = value.UserData.Object.ToString();
					}
					break;
				case DataType.Table:
					if (desiredType == typeof(Table) || desiredType.IsAssignableFrom(typeof(Table)))
						return value.Table;
					else
					{
						object o = ConvertTableToType(value.Table, desiredType);
						if (o != null)
							return o;
					}
					break;
				case DataType.Tuple:
					break;
			}

			if (str != null)
			{
				if (isString)
					return str;
				if (isStringBuilder)
					return new StringBuilder(str);
				if (isChar && str.Length > 0)
					return str[0];
			}

			throw ScriptRuntimeException.ConvertObjectFailed(value.Type, desiredType);
		}

		private static object ConvertTableToType(Table table, Type t)
		{
			if (t.IsAssignableFrom(typeof(Dictionary<object, object>)))
				return TableToDictionary<object, object>(table, v => v.ToObject(), v => v.ToObject());
			else if (t.IsAssignableFrom(typeof(Dictionary<DynValue, DynValue>)))
				return TableToDictionary<DynValue, DynValue>(table, v => v, v => v);
			else if (t.IsAssignableFrom(typeof(List<object>)))
				return TableToList<object>(table, v => v.ToObject());
			else if (t.IsAssignableFrom(typeof(List<DynValue>)))
				return TableToList<DynValue>(table, v => v);
			else if (t.IsAssignableFrom(typeof(object[])))
				return TableToList<object>(table, v => v.ToObject()).ToArray();
			else if (t.IsAssignableFrom(typeof(DynValue[])))
				return TableToList<DynValue>(table, v => v).ToArray();

			return null;
		}

		private static List<T> TableToList<T>(Table table, Func<DynValue, T> converter)
		{
			List<T> lst = new List<T>();

			for (int i = 1, l = table.Length; i <= l; i++)
			{
				DynValue v = table.Get(i);
				T o = converter(v);
				lst.Add(o);
			}

			return lst;
		}

		private static Dictionary<TK, TV> TableToDictionary<TK, TV>(Table table, Func<DynValue, TK> keyconverter, Func<DynValue, TV> valconverter)
		{
			Dictionary<TK, TV> dict = new Dictionary<TK, TV>();

			foreach (var kvp in table.Pairs)
			{
				TK key = keyconverter(kvp.Key);
				TV val = valconverter(kvp.Value);

				dict.Add(key, val);
			}

			return dict;
		}

		internal static object DoubleToType(Type type, double d)
		{
			type = Nullable.GetUnderlyingType(type) ?? type;

			if (type == typeof(double)) return d;
			if (type == typeof(sbyte)) return (sbyte)d;
			if (type == typeof(byte)) return (byte)d;
			if (type == typeof(short)) return (short)d;
			if (type == typeof(ushort)) return (ushort)d;
			if (type == typeof(int)) return (int)d;
			if (type == typeof(uint)) return (uint)d;
			if (type == typeof(long)) return (long)d;
			if (type == typeof(ulong)) return (ulong)d;
			if (type == typeof(float)) return (float)d;
			if (type == typeof(decimal)) return (decimal)d;
			return d;
		}

		internal static double TypeToDouble(Type type, object d)
		{
			if (type == typeof(double)) return (double)d;
			if (type == typeof(sbyte)) return (double)(sbyte)d;
			if (type == typeof(byte)) return (double)(byte)d;
			if (type == typeof(short)) return (double)(short)d;
			if (type == typeof(ushort)) return (double)(ushort)d;
			if (type == typeof(int)) return (double)(int)d;
			if (type == typeof(uint)) return (double)(uint)d;
			if (type == typeof(long)) return (double)(long)d;
			if (type == typeof(ulong)) return (double)(ulong)d;
			if (type == typeof(float)) return (double)(float)d;
			if (type == typeof(decimal)) return (double)(decimal)d;
			return (double)d;
		}
	}
}
