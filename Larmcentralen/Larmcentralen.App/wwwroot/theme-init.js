// Runs before the first paint so a forced light or dark theme does not flash.
// The key and values match Larmcentralen.Client.UI/wwwroot/js/theme.js.
(function () {
  try {
    var mode = localStorage.getItem("larmcentralen-theme");
    if (mode === "light" || mode === "dark") document.documentElement.dataset.theme = mode;
  } catch (e) { /* storage unavailable: follow the OS */ }
})();
