// Check if the modal already exists
var modal = document.getElementById("myModal");
if (!modal) {
    // Create the modal dialog
    modal = document.createElement("div");
    modal.id = "myModal";
    modal.className = "modal";

    // Create the content container
    var content = document.createElement("div");
    content.className = "modal-content";

    // Create the integer input
    var integerLabel = document.createElement("label");
    integerLabel.for = "integer";
    integerLabel.textContent = "Number of Bookmarks to get:";
    var num2Get = document.createElement("input");
    num2Get.type = "number";
    num2Get.id = "num2Get";
    num2Get.name = "num2Get";

    // Create the boolean input
    var booleanLabel = document.createElement("label");
    booleanLabel.for = "unbookmark";
    booleanLabel.textContent = "Unbookmark imported statuses?";
    var unbookmark = document.createElement("input");
    unbookmark.type = "checkbox";
    unbookmark.id = "unbookmark";
    unbookmark.name = "unbookmark";

    // Create the submit button
    var btn = document.createElement("button");
    btn.id = "submit";
    btn.textContent = "Submit";

    // Append the elements
    content.appendChild(integerLabel);
    content.appendChild(num2Get);
    content.appendChild(booleanLabel);
    content.appendChild(unbookmark);
    content.appendChild(btn);
    modal.appendChild(content);
    document.body.appendChild(modal);
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