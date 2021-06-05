using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace DynamicGroupBy
{
	public static class GroupByBuilder
	{
		private static Type BuildGroupingType(List<Tuple<string, Type>> objectProps) {
			var objBuilder = new MyClassBuilder($"GroupBy_{Guid.NewGuid()}");
			return objBuilder.CreateType(objectProps);
		}

		public static IQueryable<Tuple<object, int>> BuildExpression<TElement>(this IQueryable<TElement> source, DbContext context, List<string> columnNames)
		{
			var entityParameter = Expression.Parameter(typeof(TElement));
			var sourceParameter = Expression.Parameter(typeof(IQueryable<TElement>));

			var model = context.Model.FindEntityType(typeof(TElement)); // start with our own entity
			var props = model.GetPropertyAccessors(entityParameter); // get all available field names including navigations			

			var objectProps = new List<Tuple<string, Type>>();
			var accessorProps = new List<Tuple<string, Expression>>();
			var groupKeyDictionary = new Dictionary<object, string>();
			foreach (var prop in props.Where(p => columnNames.Contains(p.Item3)))
			{
				var propName = prop.Item3.Replace(".", "_"); // we need some form of cross-reference, this seems to be good enough
				objectProps.Add(new Tuple<string, Type>(propName, (prop.Item2 as MemberExpression).Type));
				accessorProps.Add(new Tuple<string, Expression>(propName, prop.Item2));
			}

			var groupingType = BuildGroupingType(objectProps); // build new type we'll use for grouping. think `new Test() { A=, B=, C= }`

			// finally, we're ready to build our expressions
			var groupbyCall = BuildGroupBy<TElement>(sourceParameter, entityParameter, accessorProps, groupingType); // source.GroupBy(s => new Test(A = s.Field1, B = s.Field2 ... ))
			var selectCall = groupbyCall.BuildSelect<TElement>(groupingType); // .Select(g => new Tuple<object, int> (g.Key, g.Count()))
			
			var lambda = Expression.Lambda<Func<IQueryable<TElement>, IQueryable<Tuple<object, int>>>>(selectCall, sourceParameter);
			return lambda.Compile()(source);
		}

		private static MethodCallExpression BuildSelect<TElement>(this MethodCallExpression groupbyCall, Type groupingAnonType) 
		{	
			var groupingType = typeof(IGrouping<,>).MakeGenericType(groupingAnonType, typeof(TElement));
			var selectMethod = QueryableMethods.Select.MakeGenericMethod(groupingType, typeof(Tuple<object, int>));
			var resultParameter = Expression.Parameter(groupingType);

			var countCall = BuildCount<TElement>(resultParameter);
			var resultSelector = Expression.New(typeof(Tuple<object, int>).GetConstructors().First(), Expression.PropertyOrField(resultParameter, "Key"), countCall);

			return Expression.Call(selectMethod, groupbyCall, Expression.Lambda(resultSelector, resultParameter));
		}

		private static MethodCallExpression BuildGroupBy<TElement>(ParameterExpression sourceParameter, ParameterExpression entityParameter, List<Tuple<string, Expression>> accessorProps, Type groupingAnonType) 
		{
			var groupByMethod = QueryableMethods.GroupByWithKeySelector.MakeGenericMethod(typeof(TElement), groupingAnonType);
			var groupBySelector = Expression.Lambda(Expression.MemberInit(Expression.New(groupingAnonType.GetConstructors().First()),
					accessorProps.Select(op => Expression.Bind(groupingAnonType.GetMember(op.Item1)[0], op.Item2))
				), entityParameter);

			return Expression.Call(groupByMethod, sourceParameter, groupBySelector);
		}

		private static MethodCallExpression BuildCount<TElement>(ParameterExpression resultParameter)
		{
			var asQueryableMethod = QueryableMethods.AsQueryable.MakeGenericMethod(typeof(TElement));
			var countMethod = QueryableMethods.CountWithoutPredicate.MakeGenericMethod(typeof(TElement));

			return Expression.Call(countMethod, Expression.Call(asQueryableMethod, resultParameter));
		}

		private static IEnumerable<Tuple<IProperty, Expression, string>> GetPropertyAccessors(this IEntityType model, Expression param, string context = "")
		{
			var result = new List<Tuple<IProperty, Expression, string>>();
			context = string.IsNullOrWhiteSpace(context) ? RelationalEntityTypeExtensions.GetTableName(model) : $"{context}.{RelationalEntityTypeExtensions.GetTableName(model)}";
			result.AddRange(model.GetProperties()
										.Where(p => !p.IsShadowProperty()) // this is your chance to ensure property is actually declared on the type before you attempt building Expression
										.Select(p => new Tuple<IProperty, Expression, string>(p, 
													Expression.Property(param, p.Name),
													
													$"{context}.{Microsoft.EntityFrameworkCore.RelationalPropertyExtensions.GetColumnName(p)}"))); // Tuple is a bit clunky but hopefully conveys the idea

			foreach (var nav in model.GetNavigations().Where(p => p is Navigation))
			{
				var parentAccessor = Expression.Property(param, nav.Name); // define a starting point so following properties would hang off there
				result.AddRange(GetPropertyAccessors(nav.ForeignKey.PrincipalEntityType, parentAccessor, context)); //recursively call ourselves to travel up the navigation hierarchy
			}

			return result;
		}
	}
}
