

// function that is called when _edit.list.html is called that
// gets the listid from the url querystring and then 
// populates the form in _edit.list.html with the list data
function getList() {
    console.log('getList')

    if (islocal()) return;

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


    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.list.html'));
    console.debug(' | API URL: ' + apiUrl);
    // Get the list data from the API
    fetch(apiUrl + '/showlists/' + listid)
        .then(response => {
            console.log('Response IS:', response);
            if (response.status == RC.UNAUTHORIZED) {
                console.debug(' | GETLIST - Not authorized to get list ' + listid);
                throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                console.debug(' | GETLIST - Forbidden to get list ' + listid);
                throw new Error('Forbidden! Are you logged in as an admin?');
            }
            else if (response.status == RC.NOT_FOUND) {
                console.debug(' | GETLIST - List ' + listid + ' not found');
                throw new Error('List ' + listid + 'not found! Have you created it yet?');
            }
            else if (response.status == RC.BAD_REQUEST) {
                console.debug(' | GETLIST - Bad request for list ' + listid);
                throw new Error('Malformed request for list (should be an number)' + listid);
            }
            else {
                return response.json();
            }

        })
        .then(json => {

            console.log('Success:', json);
            // Populate the form with the data from the API
            document.getElementById('list.id').value = json.id;
            document.getElementById('list.name').value = json.name;
            document.getElementById('list.name.original').value = json.name;
            // reason we need to pick up on the original is so that we can
            // change the url back to the NEW name of the list page on rename.
            document.getElementById('list.comment').value = json.comment;
            document.getElementById('back2list').href = apiUrl + '/' + json.name + '.html';
            d('List ' + json.id + ' retreived!');
            c(RC.OK);
        })
        .catch((error) => {
            // write any error to the span with id="result"
            d(error);
            c(RC.ERROR);
        });

}

async function updateList(e) {
    e.preventDefault();
    if (islocal()) return;
    console.log('updateList');
    let apiUrl = "";


    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.list.html'));
    myUrl = apiUrl;
    console.debug(' | API URL: ' + apiUrl);

    let id = document.getElementById('list.id').value;
    let name = document.getElementById('list.name').value;
    let comment = document.getElementById('list.comment').value;

    console.debug(' | id: ' + id);
    let data;
    let apiMethod;
    let addNotModify = false;
    // if id is null or empty, then this is a new list
    // and we need to call the API to create a new list
    if (id == null || id == '') {
        apiUrl = apiUrl + '/addlist';
        data = { name, comment };
        apiMethod = 'POST';
        addNotModify = true;
    }
    else {
        // if id is not null or empty, then this is an existing list
        // and we need to call the API to update the list
        apiUrl = apiUrl + '/modifylist';
        data = { id, name, comment };
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
        .then(response => {
            console.log('Response IS:', response);
            if (response.ok) {
                return response;
            }
            else if (response.status == RC.UNAUTHORIZED) {
                console.debug(' | ADD/EDIT LIST - Unauthorized');
                throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                console.debug(' | ADD/EDIT LIST - Forbidden');
                throw new Error('Forbidden! Are you logged in as SuperUser or Listowner?');
            }
            else if (response.status == RC.BAD_REQUEST) {
                console.debug(' | ADD/EDIT LIST - Bad request');
                return response.text().then(error => {
                    throw new Error('Bad request! ' + error);
                
                });
            }
            else {
                d('Error: ' + response.statusText);
                c(RC.ERROR);
            }
        })
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
                        document.getElementById('back2list').href = myUrl + '/' + name + '.html';
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


document.addEventListener('DOMContentLoaded', getList);

// When the form is submitted, send it to the REST API
document.getElementById('editlistform').addEventListener('submit', updateList);
