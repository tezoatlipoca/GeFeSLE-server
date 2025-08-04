// Check if the modal already exists
var modal = document.getElementById("myModal");
var num2Get, unbookmark, btn;

if (!modal) {
    // Create the modal dialog
    modal = document.createElement("div");
    modal.id = "myModal";
    modal.className = "reportModal"; // Reuse report modal styling

    // Create the content container
    var content = document.createElement("div");
    content.className = "reportModal-content"; // Reuse report modal content styling

    // Create the integer input
    var integerLabel = document.createElement("label");
    integerLabel.htmlFor = "num2Get";
    integerLabel.textContent = "Number of Bookmarks to get:";
    
    num2Get = document.createElement("input");
    num2Get.type = "number";
    num2Get.id = "num2Get";
    num2Get.name = "num2Get";
    num2Get.value = "10"; // Default value
    num2Get.min = "1";
    num2Get.max = "100";

    // Create the boolean input with better styling
    var booleanLabel = document.createElement("label");
    booleanLabel.htmlFor = "unbookmark";
    booleanLabel.textContent = "Unbookmark imported statuses?";
    
    var checkboxContainer = document.createElement("div");
    checkboxContainer.style.marginBottom = "20px";
    checkboxContainer.style.display = "flex";
    checkboxContainer.style.alignItems = "center";
    checkboxContainer.style.gap = "10px";
    
    unbookmark = document.createElement("input");
    unbookmark.type = "checkbox";
    unbookmark.id = "unbookmark";
    unbookmark.name = "unbookmark";
    unbookmark.style.transform = "scale(1.2)";

    var checkboxLabel = document.createElement("span");
    checkboxLabel.textContent = "Yes, remove bookmarks after import";
    checkboxLabel.style.fontSize = "14px";

    // Create the submit button
    btn = document.createElement("button");
    btn.id = "submit";
    btn.textContent = "Import Bookmarks";

    // Append the elements
    content.appendChild(integerLabel);
    content.appendChild(num2Get);
    content.appendChild(booleanLabel);
    checkboxContainer.appendChild(unbookmark);
    checkboxContainer.appendChild(checkboxLabel);
    content.appendChild(checkboxContainer);
    content.appendChild(btn);
    modal.appendChild(content);
    document.body.appendChild(modal);
} else {
    // Modal already exists, get references to the elements
    num2Get = document.getElementById("num2Get");
    unbookmark = document.getElementById("unbookmark");
    btn = document.getElementById("submit");
}

function showModalAndGetValues() {
    return new Promise((resolve) => {
      // Show the modal
      modal.style.display = "block";
  
      // When the user clicks the button, get the values and hide the modal
      btn.onclick = function() {
        var integer = Number(num2Get.value);
        var boolean = Boolean(unbookmark.checked);
        console.log("num2Get: " + integer);
        console.log("unbookmark: " + boolean);
        modal.style.display = "none";
  
        // Resolve the promise with the values
        resolve({num2Get: integer, unbookmark: boolean});
      }
  
      // When the user clicks anywhere outside of the modal, hide it
      window.onclick = function(event) {
        if (event.target == modal) {
          modal.style.display = "none";
  
          // Resolve the promise with null
          resolve(null);
        }
      }
    });
  }

  function mastoBookmarkSaveDefaults() {
    let fn = 'mastoBookmarkSaveDefaults'; console.info(fn);
    let num2Get = document.getElementById('num2Get').value;
    let unbookmark = document.getElementById('unbookmark').checked;
    console.debug(`${fn} -- saving num2Get: ${num2Get}, unbookmark: ${unbookmark}`);
    localStorage.setItem('num2Get', num2Get);
    localStorage.setItem('unbookmark', unbookmark);
  }
  
  function mastoBookmarkLoadDefaults() {
    let fn = 'mastoBookmarkLoadDefaults'; console.info(fn);
    let num2Get = localStorage.getItem('num2Get');
    let unbookmark = localStorage.getItem('unbookmark') === 'true'; // I hate javascript "truthy""Falsy" values
    console.debug(`${fn} -- loading num2Get: ${num2Get}, unbookmark: ${unbookmark}`);
    // the two controls will exist at this pt. we don't care if those values
    // if the values are null, but we skip so the fields use their defaults
    document.getElementById('num2Get').value = num2Get;
    document.getElementById('unbookmark').checked = unbookmark;
    
  }