// Check if the modal already exists
var reportModal = document.getElementById("reportModal");
if (!reportModal) {
  // Create the modal dialog
  reportModal = document.createElement("div");
  reportModal.id = "reportModal";
  reportModal.className = "reportModal";

  // Create the content container
  var reportcontent = document.createElement("div");
  reportcontent.className = "reportModal-content";

  var reportLabel = document.createElement("reportlabel");
  reportLabel.for = "reportreason";
  reportLabel.textContent = "Why report this item? + your contact info (if you haven't logged in)";


  // Create the text field
  var reportreason = document.createElement("input");
  reportreason.id = "reportreason";
  reportreason.name = "reportreason";



  // Create the submit button
  var reportbtn = document.createElement("button");
  reportbtn.id = "submit";
  reportbtn.textContent = "Submit";

  // Append the elements
  reportcontent.appendChild(reportLabel);
  reportcontent.appendChild(reportreason);
  reportcontent.appendChild(reportbtn);
  reportModal.appendChild(reportcontent);
  document.body.appendChild(reportModal);
}




async function showModalReportForm() {
    return new Promise((resolve) => {
    // Show the modal
    reportModal.style.display = "block";

    // When the user clicks the button, get the values and hide the modal
    reportbtn.onclick = function () {
      var reportwhy = reportreason.value;
      console.log("reportwhy: " + reportwhy);
      reportModal.style.display = "none";

      // Resolve the promise with the values
      resolve(reportwhy);
    }

    // When the user clicks anywhere outside of the modal, hide it
    window.onclick = function (event) {
      if (event.target == reportModal) {
        reportModal.style.display = "none";

        // Resolve the promise with null
        resolve(null);
      }
    }
  });
}


