using System.Windows;
using ResurectPhone.App.Presentation;

namespace ResurectPhone.App.Dialogs;

public sealed class AndroidTaskManagerInteraction(Func<Window?> ownerProvider)
    : IAndroidTaskManagerInteraction
{
    public bool ConfirmMemoryRelease()
    {
        var owner = ownerProvider();
        var result = owner is null
            ? MessageBox.Show(
                "ResurectPhone va demander à Android de fermer les applications en arrière-plan. " +
                "Les applications au premier plan et les services système ne seront pas forcés. Continuer ?",
                "Libérer la mémoire vive",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                "ResurectPhone va demander à Android de fermer les applications en arrière-plan. " +
                "Les applications au premier plan et les services système ne seront pas forcés. Continuer ?",
                "Libérer la mémoire vive",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
