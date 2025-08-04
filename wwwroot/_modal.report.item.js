// Check if the modal already exists
var reportModal = document.getElementById("reportModal");
var reportUserName, userNameLabel, reportreason, reportbtn;

if (!reportModal) {
  // Create the modal dialog
  reportModal = document.createElement("div");
  reportModal.id = "reportModal";
  reportModal.className = "reportModal";

  // Create the content container
  var reportcontent = document.createElement("div");
  reportcontent.className = "reportModal-content";

  // Create the userName label and field
  userNameLabel = document.createElement("label");
  userNameLabel.htmlFor = "reportUserName";
  userNameLabel.textContent = "Your contact info (email/social media handle):";

  reportUserName = document.createElement("input");
  reportUserName.id = "reportUserName";
  reportUserName.name = "reportUserName";
  reportUserName.placeholder = "email@example.com or @username";

  // Create the reason label and field
  var reportLabel = document.createElement("label");
  reportLabel.htmlFor = "reportreason";
  reportLabel.textContent = "Why report this item?";

  reportreason = document.createElement("input");
  reportreason.id = "reportreason";
  reportreason.name = "reportreason";
  reportreason.placeholder = "Reason for reporting...";

  // Create the submit button
  reportbtn = document.createElement("button");
  reportbtn.id = "submit";
  reportbtn.textContent = "Submit";

  // Append the elements
  reportcontent.appendChild(userNameLabel);
  reportcontent.appendChild(reportUserName);
  reportcontent.appendChild(reportLabel);
  reportcontent.appendChild(reportreason);
  reportcontent.appendChild(reportbtn);
  reportModal.appendChild(reportcontent);
  document.body.appendChild(reportModal);
} else {
  // Modal already exists, get references to the elements
  reportUserName = document.getElementById("reportUserName");
  userNameLabel = document.querySelector('label[for="reportUserName"]');
  reportreason = document.getElementById("reportreason");
  reportbtn = document.getElementById("submit");
}




async function showModalReportForm() {
    return new Promise(async (resolve) => {
        // Check if user is logged in using amloggedin function
        try {
            let [id, username, role] = await amloggedin();
            
            if (username) {
                // User is logged in - populate and make readonly
                reportUserName.value = username;
                reportUserName.readOnly = true;
                userNameLabel.textContent = "Logged in as:";
                reportUserName.style.backgroundColor = "#f0f0f0";
            } else {
                // User is not logged in - clear field and make editable
                reportUserName.value = "";
                reportUserName.readOnly = false;
                userNameLabel.textContent = "Your contact info (email/social media handle):";
                reportUserName.style.backgroundColor = "";
            }
        } catch (error) {
            console.debug("Could not determine login status:", error);
            // Default to not logged in state
            reportUserName.value = "";
            reportUserName.readOnly = false;
            userNameLabel.textContent = "Your contact info (email/social media handle):";
            reportUserName.style.backgroundColor = "";
        }

        // Show the modal
        reportModal.style.display = "block";

        // When the user clicks the button, get the values and hide the modal
        reportbtn.onclick = function () {
            var reportwhy = reportreason.value;
            var userName = reportUserName.value;
            console.log("reportwhy: " + reportwhy);
            console.log("userName: " + userName);
            reportModal.style.display = "none";

            // Resolve the promise with both values
            resolve({ reason: reportwhy, userName: userName });
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


