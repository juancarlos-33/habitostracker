document.addEventListener("DOMContentLoaded", function () {

    const loader = document.getElementById("loader");

    // 🔥 Mostrar loader al enviar formularios
    document.querySelectorAll("form:not([data-no-loader])").forEach(form => {
        form.addEventListener("submit", function () {
            if (loader) loader.style.display = "flex";
        });
    });

    // 🔥 Ocultar loader cuando la página carga
    window.addEventListener("load", function () {
        if (loader) loader.style.display = "none";
    });

});