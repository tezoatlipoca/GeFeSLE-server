// Check if the modal already exists
var googmodal = document.getElementById("googModal");
if (!googmodal) {
  // Create the modal dialog
  googmodal = document.createElement("div");
  googmodal.id = "googModal";
  googmodal.className = "googmodal";

  // Create the content container
  var googcontent = document.createElement("div");
  googcontent.className = "googmodal-content";

  // Create the dropdown select field
  var googselect = document.createElement("select");
  googselect.id = "googselect";
  googselect.name = "googselect";



  // Create the submit button
  var googbtn = document.createElement("button");
  googbtn.id = "submit";
  googbtn.textContent = "Submit";

  // Append the elements
  googcontent.appendChild(googselect);
  googcontent.appendChild(googbtn);
  googmodal.appendChild(googcontent);
  document.body.appendChild(googmodal);
}

function populateGoogleTaskLists(sourceService) {
  // first we want to GET a list of the user's task lists from google
  // we do this by sending the importer to /lists/query not /lists/{listid}
  let fn = 'populateGoogleTaskLists'; console.info(fn + ' <- ' + JSON.stringify(sourceService));
  importService = { Service: sourceService, Data: '' };

  let apiUrl = '/lists/query';
  let apiMethod = 'POST';
  console.info(`${fn} ${apiUrl} <- ${JSON.stringify(importService)}`);
  fetch(apiUrl, {
    method: apiMethod,
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(importService)
  })
    .then(handleResponse)
    .then(response => response.json())
    .then(data => {
      const select = document.getElementById('googselect');
      select.innerHTML = '';
      data.forEach(item => {
        console.debug(`${fn} ++ Google task: ${JSON.stringify(item)}`);
        const option = document.createElement('option');
        option.value = item.id;
        option.text = item.title;
        console.debug(`${fn} ++ option: ${item.id}:${item.title}`);
        select.appendChild(option);
      });
    })
    .catch((error) => {
      d(error);
      c(RC.ERROR);

    });
  }



function showModalGoogleTaskLists() {
    return new Promise((resolve) => {
    // Show the modal
    googmodal.style.display = "block";

    // When the user clicks the button, get the values and hide the modal
    googbtn.onclick = function () {
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


