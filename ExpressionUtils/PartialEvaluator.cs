using MiaPlaza.ExpressionUtils.Evaluating;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace MiaPlaza.ExpressionUtils {
	public static class PartialEvaluator {
		/// <summary>
		/// Performs evaluation & replacement of independent sub-trees
		/// </summary>
		/// <param name="expression">The root of the expression tree.</param>
		/// <param name="fnCanBeEvaluated">A function that decides whether a given expression node can be part of the local function.</param>
		/// <returns>A new tree with sub-trees evaluated and replaced.</returns>
		public static Expression PartialEval(Expression expression, IExpressionEvaluator evaluator, Func<Expression, bool> fnCanBeEvaluated) 
			=> new SubtreeEvaluator(evaluator, Nominator.Nominate(expression, fnCanBeEvaluated)).Eval(expression);

		/// <summary>
		/// Performs evaluation & replacement of independent sub-trees
		/// </summary>
		/// <param name="expression">The root of the expression tree.</param>
		/// <returns>A new tree with sub-trees evaluated and replaced.</returns>
		public static TExpression PartialEval<TExpression>(TExpression expression, IExpressionEvaluator evaluator) where TExpression : Expression {
			return (TExpression)PartialEval(expression, evaluator, canBeEvaluated);
		}

		private static bool canBeEvaluated(Expression expression) {
			if (expression.NodeType == ExpressionType.Parameter) {
				return false;
			}

			MemberInfo memberAccess = (expression as MethodCallExpression)?.Method 
				?? (expression as NewExpression)?.Constructor
				?? (expression as MemberExpression)?.Member;

			return memberAccess == null || !memberAccess.IsDefined(typeof(NoPartialEvaluationAttribute), inherit: false);
		}

		/// <summary>
		/// Evaluates & replaces sub-trees when first candidate is reached (top-down)
		/// </summary>
		class SubtreeEvaluator : ExpressionVisitor {
			readonly HashSet<Expression> candidates;
			readonly IExpressionEvaluator evaluator;

			internal SubtreeEvaluator(IExpressionEvaluator evaluator, HashSet<Expression> candidates) {
				this.candidates = candidates;
				this.evaluator = evaluator;
			}

			internal Expression Eval(Expression exp) {
				return this.Visit(exp);
			}

			private int depth = -1;

			public override Expression Visit(Expression exp) {
				if (exp == null) {
					return null;
				}

				// In case we visit lambda expression, we want to return lambda expression as a result of visit,
				// so we don't want to replace root lambda node with constant expression node, so we don't do anything here.
				if (candidates.Contains(exp) && !(depth == -1 && exp is LambdaExpression)) {
					if (exp is ConstantExpression) {
						return exp;
					}

					return evaluate(exp);
				}

				depth++;
				var newNode = base.Visit(exp);
				depth--;
				return newNode;
			}

			protected override Expression VisitLambda<T>(Expression<T> node) {
				// This is root lambda node that we want to evaluate. Since we want to preserve type
				// of input expression, we will update the body, but still keep the lambda.
				if (candidates.Contains(node) && depth == 0) {
					var constant = evaluate(node.Body);
					return node.Update(constant, node.Parameters);
				}

				return base.VisitLambda(node);
			}

			private Expression evaluate(Expression exp) {
				try {
					return Expression.Constant(evaluator.Evaluate(exp), exp.Type);
				}
				catch (Exception exception) {
					return ExceptionClosure.MakeExceptionClosureCall(exception, exp.Type);
				}
			}
		}

		/// <summary>
		/// Performs bottom-up analysis to determine which nodes can possibly
		/// be part of an evaluated sub-tree.
		/// </summary>
		class Nominator : ExpressionVisitor {
			private readonly Func<Expression, bool> fnCanBeEvaluated;
			private readonly HashSet<Expression> candidates = new HashSet<Expression>();
			bool canBeEvaluated;

			private Nominator(Func<Expression, bool> fnCanBeEvaluated) {
				this.fnCanBeEvaluated = fnCanBeEvaluated;
			}

			public static HashSet<Expression> Nominate(Expression expression, Func<Expression, bool> fnCanBeEvaluated) {
				var visitor = new Nominator(fnCanBeEvaluated);
				visitor.Visit(expression);
				return visitor.candidates;
			}

			public override Expression Visit(Expression expression) {
				if (expression != null) {
					bool saveCanBeEvaluated = this.canBeEvaluated;
					this.canBeEvaluated = true;
					base.Visit(expression);
					if (this.canBeEvaluated) {
						if (this.fnCanBeEvaluated(expression)) {
							this.candidates.Add(expression);
						} else {
							this.canBeEvaluated = false;
						}
					}
					this.canBeEvaluated &= saveCanBeEvaluated;
				}
				return expression;
			}
		}
	}
}
