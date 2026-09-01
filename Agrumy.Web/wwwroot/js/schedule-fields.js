// Roadmap #39: ASP.NET Core model binding has no native "N checkboxes -> one bitmask int" support,
// so each day-of-week picker (_ScheduleField.cshtml) posts through a hidden <input>, kept in sync
// with its checkboxes here instead of a bespoke model binder or a submit-time handler per group.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-schedule-days-for]').forEach(function (group) {
        var fieldName = group.getAttribute('data-schedule-days-for');
        var hidden = document.querySelector('[data-schedule-days-hidden="' + fieldName + '"]');
        if (!hidden) {
            return;
        }

        function sync() {
            var mask = 0;
            group.querySelectorAll('input[type=checkbox]').forEach(function (checkbox) {
                if (checkbox.checked) {
                    mask |= parseInt(checkbox.value, 10);
                }
            });
            hidden.value = mask;
        }

        group.addEventListener('change', sync);
    });
});
