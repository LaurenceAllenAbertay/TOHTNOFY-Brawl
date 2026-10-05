/* 
    JavaScript to control project fading and screenshot slideshows
    Operates by simply adding and removing CSS classes
*/

// --- Fade Projects ---

// Mark the page as having JavaScript
document.documentElement.classList.add("js");

// Intersection Observer reports when an element enters or leaves the viewport
const watcher = new IntersectionObserver(entries => {
    entries.forEach(entry => {
        entry.target.classList.toggle("above", entry.boundingClientRect.top < 0);
        entry.target.classList.toggle("seen", entry.isIntersecting);
    });
}, { rootMargin: "-80px 0px -80px 0px" }); // Waits until 80pxs off screen

// Watch every element marked as .reveal in the HTML
document.querySelectorAll(".reveal").forEach(row => watcher.observe(row));

// --- Screenshot Slidehows ---

document.querySelectorAll(".shots").forEach(shots => {
    const images = shots.querySelectorAll(".shot");
    const dotBox = shots.querySelector(".dots");
    let index = 0;

    if (images.length < 2) {
        shots.classList.add("solo");
        return;
    }

    // Builds 1 dot per image
    images.forEach((image, i) => {
        const dot = document.createElement("button");
        dot.type = "button";
        dot.addEventListener("click", () => show(i));
        dotBox.append(dot);
    });

    // Show next screen shot and update dots
    function show(next) {
        index = (next + images.length) % images.length;
        images.forEach((image, i) => image.classList.toggle("on", i === index));
        dotBox.querySelectorAll("button").forEach((dot, i) => dot.classList.toggle("on", i === index));
    }

    shots.querySelector(".prev").addEventListener("click", () => show(index - 1));
    shots.querySelector(".next").addEventListener("click", () => show(index + 1));

    // Set the starting state
    show(0);
});