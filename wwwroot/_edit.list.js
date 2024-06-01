

// function that is called when _edit.list.html is called that
// gets the listid from the url querystring and then 
// populates the form in _edit.list.html with the list data
async function getList() {
    let fn = 'getList';
    console.log(fn);

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    
    if (!isSuperUser(role) && !isListOwner(role)){
        d("Not logged in!");
        c(RC.UNAUTHORIZED);
        return;
    }

    // Get the listid from the querystring
    let urlParams = new URLSearchParams(window.location.search);
    let listid = urlParams.get('listid');
    console.debug(' | listid: ' + listid);
    if (listid == null || listid == '') {
        // write any error to the span with id="result"
        d('No List Id - creating new');
        c(RC.OK);
        return;
    }


    let apiUrl = "/lists/" + listid;
    console.debug(fn + ' API URL: ' + apiUrl);
    // Get the list data from the API
    fetch(apiUrl)
        .then(handleResponse)
        .then(response => response.json())
        .then(json => {

            console.log('Success:', json);
            // Populate the form with the data from the API
            document.getElementById('list.id').value = json.id;
            document.getElementById('list.name').value = json.name;
            document.getElementById('list.name.original').value = json.name;
            // set the visibility select to the visibility of the list
            document.getElementById('list.visibility').value = json.visibility;
            // reason we need to pick up on the original is so that we can
            // change the url back to the NEW name of the list page on rename.
            easymde.value(json.comment);
            //document.getElementById('list.comment').value = json.comment;
            document.getElementById('back2list').href = json.name + '.html';
            d('List ' + json.id + ' retreived!');
            c(RC.OK);
            getListUsers();
        })
        .catch((error) => {
            // write any error to the span with id="result"
            d(error);
            c(RC.ERROR);
        });

}

async function updateList(e) {
    let fn = 'updateList';
    console.log('updateList');
    e.preventDefault();
    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let [userid, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)){
        d("Not logged in!");
        c(RC.UNAUTHORIZED);
        return;
    }

    let id = document.getElementById('list.id').value;
    let name = document.getElementById('list.name').value;
    //let comment = document.getElementById('list.comment').value;
    let comment = easymde.value();
    let visibility = document.getElementById('list.visibility').value;

    console.debug(' | id: ' + id);
    let data;
    let apiMethod;
    let addNotModify = false;
    // if id is null or empty, then this is a new list
    // and we need to call the API to create a new list
    let apiUrl = '/lists';
    if (id == null || id == '') {
        data = { name, comment, visibility };
        apiMethod = 'POST';
        addNotModify = true;
    }
    else {
        // if id is not null or empty, then this is an existing list
        // and we need to call the API to update the list
        data = { id, name, comment, visibility };
        apiMethod = 'PUT';
    }
    let displayResults = "";
    let newID = null;
    // Call the REST API
    console.debug(' | Calling API: ' + apiUrl + ' with data: ' + JSON.stringify(data));
    fetch(apiUrl, {
        method: apiMethod,
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(data),
    })
        .then(handleResponse)
        // by now, response is either json or text
        .then(response => {
            const contentType = response.headers.get("content-type");
            console.log('Content-Type IS:', contentType);
            if (contentType && contentType.includes("application/json")) {
                return response.json().then(json => {
                    //
                    newID = json.id;
                    console.debug('NEW ID IS:', newID);
                    displayResults = "List Created: " + JSON.stringify(json);
                    console.debug('displayResults IS:', displayResults);
                    d(displayResults);
                    c(RC.OK);

                    // now that we have newID, populdate the id field in the form
                    document.getElementById('list.id').value = newID;

                    // and update the url in the browser to include the newID
                    let newUrl = window.location.href;
                    newUrl = newUrl.substring(0, newUrl.indexOf('?'));
                    newUrl = newUrl + '?listid=' + newID;
                    window.history.pushState({}, '', newUrl);
                    console.debug(' | New URL: ' + newUrl);
                    //return response.json();
                });
            } else {
                return response.text().then(text => {
                    displayResults = "List modified! ";
                    console.debug('displayResults IS:', displayResults);
                    d(displayResults);
                    c(RC.OK);
                    // if the name has changed, then we need to update back2list
                    if (document.getElementById('list.name.original').value != name) {
                        document.getElementById('back2list').href = name + '.html';
                        console.debug(' | back2list.href: ' + document.getElementById('back2list').href);
                    }
                });
            }
        })

        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });
}

async function getAllUsers() {
    let fn = 'getAllUsers';
    
    console.log(fn);
    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)){
        d("Not logged in!");
        c(RC.UNAUTHORIZED);
        return;
    }

    let apiUrl = "/users";
    console.debug(fn + ' | API URL: ' + apiUrl);
    await fetch(apiUrl, {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
    })
        .then(handleResponse)
        .then(response => response.json())
        .then(json => {
            // json is a list of users. Put userName and email values into a dictionary
            // iterate over every user collection in the json
            let users = {};
            for (let i = 0; i < json.length; i++) {
                users[json[i].userName] = json[i].email;
                // add each user to the list.allusers select; option label is "userName (email)", value is username
                let option = document.createElement("option");
                option.text = json[i].userName + " (" + (json[i].email || "<no email>") + ")";
                option.value = json[i].userName;
                document.getElementById('list.allusers').add(option);
            }
        })
        .catch((error) => {
            console.error(error);
            d(error);
            c(RC.ERROR);
        });

}

async function getListUsers() {
    let fn = 'getListUsers';
    //e.preventDefault();
    console.log(fn);
    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)){
        d("Not logged in!");
        c(RC.UNAUTHORIZED);
        return;
    }

    // get the value of the listid out of the list.id field
    let listid = document.getElementById('list.id').value;

    let apiUrl = "/getlistuser/" + listid; 
    await fetch(apiUrl, {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
    })
        .then(handleResponse)
        .then(response => response.json())
        .then(json => {
            // json is a list of users. Put userName and email values into a dictionary
            // structure of json is: {creator, listowners[], contributors[]} and each of these is a user object
            // get the creator
            let creator = json.creator;
            // display creator.userName (creator.email) in the list.creator span
            document.getElementById('listcreator').innerText = creator.userName + " (" + (creator.email || "<no email>") + ")";
            
            let listowners = json.listowners;
            let listownersVar = "";
            // iterate over every listowner in the listowners array
            for (let i = 0; i < listowners.length; i++) {
                // add each listowner to the listowners span; 
                listownersVar += listowners[i].userName + " (" + (listowners[i].email || "<no email>") + ") ";
            }
            if(listownersVar.length == 0) {
                listownersVar = "No listowners yet!";
            }
            document.getElementById('listowners').innerText = listownersVar;
            
            let contributors = json.contributors;
            let contributorsVar = "";
            // iterate over every contributor in the contributors array
            for (let i = 0; i < contributors.length; i++) {
                // add each contributor to the contributors span; 
                contributorsVar += contributors[i].userName + " (" + (contributors[i].email|| "<no email>") + ") ";
            }
            if(contributorsVar.length == 0) {
                contributorsVar = "No contributors yet!";
            }
            document.getElementById('listcontributors').innerText = contributorsVar;
        })
        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });

}

async function assignUser2List(e) {
    let fn = 'assignUser2List';
    e.preventDefault();
    console.log(fn);
    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)){
        d("Not logged in!");
        c(RC.UNAUTHORIZED);
        return;
    }

    // get the value of the listid out of the list.id field
    let listid = document.getElementById('list.id').value;

    // get the value of the username out of the list.allusers select
    let assignee = document.getElementById('list.allusers').value;

    // get the role of the user from the list.role select
    let assignee_role = document.getElementById('list.userrole').value;

    let apiUrl = "/setlistuser";
    
    // create post form data with listid, assignee as "username" and role
    let data = {
        'listid': listid,
        'username': assignee,
        'role': assignee_role
    };

    console.debug(' | API URL: ' + apiUrl);
    console.debug(' | Data: ' + data);
    
    await fetch(apiUrl, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(data),
    })
        .then(handleResponse)
        .then(response => response.text())
        .then(text => {
            // text is a message from the API
            d(text);
            c(RC.OK);
            // call getListUsers to update the list of users
            getListUsers(e);
        })
        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });

}

async function removeUserFromList(e) {
    let fn = 'removeUserFromList';
    e.preventDefault();
    console.log('removeUserFromList');
    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)){
        d("Not logged in!");
        c(RC.UNAUTHORIZED);
        return;
    }

    // get the value of the listid out of the list.id field
    let listid = document.getElementById('list.id').value;

    // get the value of the username out of the list.allusers select
    let assignee = document.getElementById('list.allusers').value;

    // get the role of the user from the list.role select
    let assignee_role = document.getElementById('list.userrole').value;

    let apiUrl = "/deletelistuser";
    
    // create post form data with listid, assignee as "username" and role
    let data = {
        'listid': listid,
        'username': assignee,
        'role': assignee_role
    };

    console.debug(' | API URL: ' + apiUrl);
    console.debug(' | Data: ' + data);
    
    await fetch(apiUrl, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(data),
    })
        .then(handleResponse)
        .then(response => response.text())
        .then(text => {
            // text is a message from the API
            d(text);
            c(RC.OK);
            // call getListUsers to update the list of users
            getListUsers(e);
        })
        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });

}


document.addEventListener('DOMContentLoaded', getList);
document.addEventListener('DOMContentLoaded', getAllUsers);

// When the form is submitted, send it to the REST API
document.getElementById('editlistform').addEventListener('submit', updateList);
// when the form is loaded, call the getListUsers function
document.getElementById('editlistform').addEventListener('load', getListUsers);
