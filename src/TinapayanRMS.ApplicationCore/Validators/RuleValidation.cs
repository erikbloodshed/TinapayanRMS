using FluentValidation;
using FluentValidation.Results;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// Runs a business rule as a validation rule. A check the repositories enforce is written once, in the Rules, as a method
/// that throws BusinessRuleException; a validator runs it through <see cref="Satisfies"/> and fails with the rule's own
/// words, so a screen's pre-check and the save it guards can't drift apart. A check with no rule behind it, such as a pick
/// a form must make, stays the validator's own.
/// </summary>
internal static class RuleValidation
{
    public static IRuleBuilderOptionsConditions<T, TProperty> Satisfies<T, TProperty>(
        this IRuleBuilder<T, TProperty> ruleBuilder, Action<TProperty> rule) =>
        ruleBuilder.Custom((value, context) =>
        {
            try
            {
                rule(value);
            }
            catch (BusinessRuleException ex)
            {
                // As it is: AddFailure(string) would read a name's braces as a message placeholder.
                context.AddFailure(new ValidationFailure(context.PropertyPath, ex.Message));
            }
        });
}
