// Roadmap #39/#115: schedule window rows (_ScheduleField.cshtml/_ScheduleSlotRow.cshtml).
// Everything here is event-delegated (document-level listeners, not per-element ones wired once
// at page load) because rows can be added/removed after the page renders - a listener attached
// only to the rows present at DOMContentLoaded would silently miss every row added afterward.

// Day-of-week checkboxes -> the row's own hidden bitmask input. ASP.NET Core model binding has no
// native "N checkboxes -> one int" support, so this replaces a bespoke model binder. Scoped to the
// row via [data-schedule-row], not by matching an exact field-name string - the field's name only
// matters for what gets POSTed, not for wiring the checkboxes to their own hidden input.
document.addEventListener('change', function (event) {
    if (!event.target.matches('[data-schedule-day-checkbox]')) {
        return;
    }
    var row = event.target.closest('[data-schedule-row]');
    var hidden = row.querySelector('[data-schedule-days-hidden]');
    var mask = 0;
    row.querySelectorAll('[data-schedule-day-checkbox]').forEach(function (checkbox) {
        if (checkbox.checked) {
            mask |= parseInt(checkbox.value, 10);
        }
    });
    hidden.value = mask;
});

document.addEventListener('click', function (event) {
    var addBtn = event.target.closest('[data-schedule-add-row]');
    if (addBtn) {
        var group = addBtn.closest('[data-schedule-group]');
        var rowsContainer = group.querySelector('[data-schedule-rows]');
        var template = group.querySelector('template');
        var nextIndex = rowsContainer.children.length;
        // The template partial is rendered with Index = -1 (see _ScheduleField.cshtml) - swap
        // that sentinel for the real next index in both name="...[-1]..." and id/for="..._-1_...".
        var html = template.innerHTML
            .split('[-1]').join('[' + nextIndex + ']')
            .split('_-1_').join('_' + nextIndex + '_');
        var wrapper = document.createElement('div');
        wrapper.innerHTML = html.trim();
        rowsContainer.appendChild(wrapper.firstElementChild);
        return;
    }

    var removeBtn = event.target.closest('[data-schedule-remove-row]');
    if (removeBtn) {
        var row = removeBtn.closest('[data-schedule-row]');
        var rowsContainer = row.parentElement;
        row.remove();
        // MVC's default collection model binder requires contiguous 0-based indices - a gap left
        // by a removed middle row would silently stop binding every row after it.
        Array.from(rowsContainer.children).forEach(function (r, i) {
            r.querySelectorAll('[name]').forEach(function (el) {
                el.name = el.name.replace(/\[\d+\]/, '[' + i + ']');
            });
            r.querySelectorAll('[id]').forEach(function (el) {
                el.id = el.id.replace(/_\d+_/, '_' + i + '_');
            });
            r.querySelectorAll('label[for]').forEach(function (el) {
                el.htmlFor = el.htmlFor.replace(/_\d+_/, '_' + i + '_');
            });
        });
    }
});
