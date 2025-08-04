// Check if the modal already exists
var googmodal = document.getElementById("googModal");
var googselect, googbtn;

if (!googmodal) {
  // Create the modal dialog
  googmodal = document.createElement("div");
  googmodal.id = "googModal";
  googmodal.className = "reportModal"; // Reuse report modal styling

  // Create the content container
  var googcontent = document.createElement("div");
  googcontent.className = "reportModal-content"; // Reuse report modal content styling

  // Create label for the dropdown
  var googLabel = document.createElement("label");
  googLabel.htmlFor = "googselect";
  googLabel.textContent = "Select Google Task List to import:";

  // Create the dropdown select field
  googselect = document.createElement("select");
  googselect.id = "googselect";
  googselect.name = "googselect";
  // Apply consistent styling to match input fields
  googselect.style.width = "100%";
  googselect.style.padding = "10px";
  googselect.style.marginBottom = "20px";
  googselect.style.border = "1px solid #ddd";
  googselect.style.borderRadius = "4px";
  googselect.style.fontSize = "14px";
  googselect.style.boxSizing = "border-box";

  // Create the submit button
  googbtn = document.createElement("button");
  googbtn.id = "submit";
  googbtn.textContent = "Import Selected List";

  // Append the elements
  googcontent.appendChild(googLabel);
  googcontent.appendChild(googselect);
  googcontent.appendChild(googbtn);
  googmodal.appendChild(googcontent);
  document.body.appendChild(googmodal);
} else {
  // Modal already exists, get references to the elements
  googselect = document.getElementById("googselect");
  googbtn = document.getElementById("submit");
}

function populateGoogleTaskLists(sourceService) {
  // first we want to GET a list of the user's task lists from google
  // we do this by sending the importer to /lists/query not /lists/{listid}
  let fn = 'populateGoogleTaskLists'; console.info(fn + ' <- ' + JSON.stringify(sourceService));
  importService = { Service: sourceService, Data: '' };

  let apiUrl = '/lists/query';
  let apiMethod = 'POST';
  console.info(`${fn} ${apiUrl} <- ${JSON.stringify(importService)}`);
  return fetch(apiUrl, {
    method: apiMethod,
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(importService)
  })
    .then(response => {
      if (!response.ok) {
        // Check if it's an authentication error
        if (response.status === 401 || response.status === 403) {
          throw new Error('NOT_AUTHENTICATED');
        }
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      return response.json();
    })
    .then(data => {
      const select = document.getElementById('googselect');
      select.innerHTML = '';
      
      // Check if user has no task lists
      if (!data || data.length === 0) {
        const option = document.createElement('option');
        option.value = '';
        option.text = 'No Google Task Lists found';
        option.disabled = true;
        select.appendChild(option);
        return false; // Indicate no lists available
      }
      
      // Populate with actual task lists
      data.forEach(item => {
        console.debug(`${fn} ++ Google task: ${JSON.stringify(item)}`);
        const option = document.createElement('option');
        option.value = item.id;
        option.text = item.title;
        console.debug(`${fn} ++ option: ${item.id}:${item.title}`);
        select.appendChild(option);
      });
      return true; // Indicate lists were successfully loaded
    })
    .catch((error) => {
      const select = document.getElementById('googselect');
      select.innerHTML = '';
      
      if (error.message === 'NOT_AUTHENTICATED') {
        const option = document.createElement('option');
        option.value = '';
        option.text = "You're not logged in w/ Google";
        option.disabled = true;
        select.appendChild(option);
      } else {
        const option = document.createElement('option');
        option.value = '';
        option.text = 'Error loading Google Task Lists';
        option.disabled = true;
        select.appendChild(option);
      }
      
      d(error);
      c(RC.ERROR);
      return false; // Indicate failure
    });
  }



function showModalGoogleTaskLists() {
    return new Promise((resolve) => {
    // Show the modal
    googmodal.style.display = "block";

    // Check if there are any valid options (not disabled)
    const validOptions = Array.from(googselect.options).filter(option => !option.disabled && option.value !== '');
    const hasValidOptions = validOptions.length > 0;
    
    // Disable/enable the submit button based on available options
    googbtn.disabled = !hasValidOptions;
    if (!hasValidOptions) {
      googbtn.style.backgroundColor = '#ccc';
      googbtn.style.cursor = 'not-allowed';
    } else {
      googbtn.style.backgroundColor = '';
      googbtn.style.cursor = 'pointer';
    }

    // When the user clicks the button, get the values and hide the modal
    googbtn.onclick = function () {
      // Don't proceed if no valid options
      if (!hasValidOptions) {
        return;
      }
      
      var list = googselect.value;
      console.log("list: " + list);
      googmodal.style.display = "none";

      // Resolve the promise with the values
      resolve(list);
    }

    // When the user clicks anywhere outside of the modal, hide it
    window.onclick = function (event) {
      if (event.target == googmodal) {
        googmodal.style.display = "none";

        // Resolve the promise with null
        resolve(null);
      }
    }
  });
}


