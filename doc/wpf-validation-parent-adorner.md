# WPF Validation Parent Adorner Fix

## Symptom

Invalid scalar fields in the settings sidebar correctly turned their text red, but after a subsequent edit WPF drew a red validation rectangle around the whole sidebar column.

The red rectangle was not the `TextBox` border. It was WPF's default validation adorner rendered on the parent `SettingsPanelView`.

## Root Cause

`ConfigWindow.xaml` assigned the shared view model through structural `DataContext` bindings:

```xaml
<local:SettingsPanelView Grid.Column="0" DataContext="{Binding vm}" />
<local:BindingsPanelView Grid.Column="1" DataContext="{Binding vm}" />
```

`ConfigWindowViewModel` implements `INotifyDataErrorInfo`. With the default binding behavior, WPF can treat a binding to an `INotifyDataErrorInfo` object as validation-aware. That made the parent `UserControl` bindings participate in validation even though they were only wiring the view model into the visual tree.

When any scalar property became invalid, the parent `DataContext` binding could acquire validation state. WPF then applied its default validation error template to the parent control, producing the full-height red frame.

## Fix

Disable notify-data-error validation on structural `DataContext` bindings:

```xaml
<local:SettingsPanelView
    Grid.Column="0"
    DataContext="{Binding vm, ValidatesOnNotifyDataErrors=False}" />

<local:BindingsPanelView
    Grid.Column="1"
    DataContext="{Binding vm, ValidatesOnNotifyDataErrors=False}" />
```

The same rule applies to other structural bindings to `vm`, such as the apply button `DataContext`.

Input bindings that should show validation feedback still opt in explicitly:

```xaml
Text="{Binding oscIpText, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}"
```

## Why This Is Correct

The parent controls are not user-editable fields. Their `DataContext` bindings should not render validation state.

Validation remains scoped to the actual input bindings:

- `TextBox` validation state drives red field text.
- Field tooltips read `Validation.Errors` from the input control.
- The footer can still summarize scalar validation errors from the view model.
- WPF no longer draws an adorner around the sidebar or other parent containers.

## Rule

Use `ValidatesOnNotifyDataErrors=True` only on bindings that intentionally present validation feedback. For structural bindings, especially `DataContext` bindings to a view model that implements `INotifyDataErrorInfo`, set `ValidatesOnNotifyDataErrors=False`.
