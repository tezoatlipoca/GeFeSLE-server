
document.addEventListener('DOMContentLoaded', getItem);



// function that is called when _edit.item.html is called that
// gets the listid from the url querystring and then 
// populates the form in _edit.item.html with the list data
async function getItem() {
    let fn = 'getItem'; console.log(fn)

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
        }
    
    let [id, username, role] = await amloggedin();
        console.debug(fn + ' -- username: ' + username);
        console.debug(fn + ' -- role: ' + role);
    

    // Get the itemid from the querystring
    let urlParams = new URLSearchParams(window.location.search);
    let itemid = urlParams.get('itemid');
    console.debug(fn + ' -- itemid: ' + itemid);
    let listid = urlParams.get('listid');
    console.debug(fn + ' -- listid: ' + listid);
    // set the value of the isSuggestion field with this value
    let suggestionBox = document.getElementById('isSuggestion');
    let isSuggestion = urlParams.get('suggestion');
    console.debug(fn + ' -- suggestion? ' + isSuggestion);
        
    if (!isSuperUser(role) && !isListOwner(role)){
        isSuggestion = true;   
    }
    suggestionBox.checked = isSuggestion;
    
    if (itemid == null || itemid == '') {
        // not necessarily bad; if we have a listid, then we are creating a new item

        if (listid == null || listid == '') {
            // ok now we panic
            d('No itemid or listid provided!');
            // disable the form submit action
            document.getElementById('edititemform').addEventListener('submit', function (e) {
                e.preventDefault();
            });
            // make the form fields readonly and greyed out

            document.getElementById('item.name').readOnly = true;
            document.getElementById('item.comment').readOnly = true;
            document.getElementById('item.tags').readOnly = true;
            document.getElementById('item.visible').disabled = true;
            return;
        } else {


            // populate the listid field in the form
            document.getElementById('item.listid').value = listid;
            if(isSuggestion) {
                // get the first <h1> tag in the DOM
                let h1 = document.querySelector('h1');
                
                h1.innerText = 'SUGGEST new item in list ' + listid;
                
                document.getElementById('item.visible').disabled = true;
                document.getElementById('item.visible').checked = false;
                
                d('Creating SUGGESTION in list ' + listid + '.');
                c(RC.OK);
            } else {
                document.getElementById('item.visible').checked = true;
                d('Creating new item in list ' + listid + '.');
                c(RC.OK);
            }
        }


    }
    else {
        // we got a valid itemid, but we must also have a valid listid
        if (listid == null || listid == '') {
            // ok now we panic
            d('No listid provided!');
            // disable the form submit action
            document.getElementById('edititemform').addEventListener('submit', function (e) {
                e.preventDefault();
            });
            // make the form fields readonly and greyed out
            document.getElementById('item.name').readOnly = true;
            document.getElementById('item.comment').readOnly = true;
            document.getElementById('item.tags').readOnly = true;
            document.getElemebtById('item.visible').disabled = true;
            return;
        } else {
            // populate the listid field in the form
            document.getElementById('item.listid').value = listid;
            // populate the itemid field in the form
            document.getElementById('item.id').value = itemid;
            d('Editing item ' + itemid + ' in list ' + listid + '.');
            c(RC.OK);
            // oh and set the radio button to "update"
            document.getElementById('update').checked = true;
            
        }
    }


    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.item.html'));
    console.debug(fn + ' -- apiUrl: ' + apiUrl);
    if (itemid != null) {
        console.debug(fn + ' -- Getting item ' + itemid + ' in list ' + listid + '.')
        fetch(apiUrl + '/showitems/' + listid + '/' + itemid)
            .then(response => {
                if (response.ok) {
                    return response.json();
                }
                else  {
                    throw new Error(`${fn} - ${response.status}:${response.statusText}`);
                }
            })
            //.then(response => response.json())
            .then(data => {
                console.log('Success:', data);
                // Populate the form with the data from the API
                document.getElementById('item.id').value = data.id;
                document.getElementById('item.name').value = data.name;
                document.getElementById('item.visible').checked = data.visible;
                //document.getElementById('item.comment').value = data.comment;
                easymde.value(data.comment);
                document.getElementById('item.tags').value = data.tags.join(' ');
            })
            .catch((error) => {
                // write any error to the span with id="result"
                d(error);
                c(RC.ERROR);
                console.error(error);
            });
    }
    // Get the list NAME for the "back to list page" link
    fetch('/lists/' + listid)
        .then(handleResponse)
        .then(response => response.json())
        .then(data => {
            console.log('Success:', data);
            // Populate the form with the data from the API
            document.getElementById('back2list').href = apiUrl + '/' + data.name + '.html';
            console.log(' | back2list.href: ' + document.getElementById('back2list').href);
        })
        .catch((error) => {
            // write any error to the span with id="result"
            d(error);
            c(RC.ERROR);
            document.getElementById('back2list').href = '/index.html';
            document.getElementById('back2list').innerText = 'Nonexistant List - Back to index';
            console.error('Error:', error);
        });


}

// When the form is submitted, send it to the REST API
document.getElementById('edititemform').addEventListener('submit', updateItem);

async function updateItem(e) {
    let fn = 'updateItem';
    e.preventDefault();
    console.log('updateItem');
    let apiUrl = "";

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
        }
    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    let isSuggestion = document.getElementById("isSuggestion").checked;
    
    if (!isSuperUser(role) && !isListOwner(role) && !isSuggestion){
        d("You are not logged in! <a href='_login.html'>Login here.</a>");
        c(RC.UNAUTHORIZED);
        return;
    }



    try {
        apiUrl = window.location.href;
        // get just the hostname and port from the url
        apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.item.html'));
        myUrl = apiUrl;
        console.debug(' | API URL: ' + apiUrl);

        let id = document.getElementById('item.id').value;
        let listid = document.getElementById('item.listid').value;
        let name = document.getElementById('item.name').value;
        let visible = document.getElementById('item.visible').checked; 
        //let comment = document.getElementById('item.comment').value;
        let comment = easymde.value();
        //alert(comment);
        let tags = document.getElementById('item.tags').value;
        //split the tags 

        console.debug(' | id: ' + id);
        console.debug(' | listid: ' + listid);
        let data;
        let apiMethod;
        // if id is null or empty, then this is a new item
        // and we need to call the API to create a new item
        if (id == null || id == '') {
            
            if(isSuggestion) {
                apiUrl = '/lists/' + listid + '/suggest';
            } else {
                apiUrl = apiUrl + '/additem/' + listid;
            }
            data = { listid, name, comment, tags: tags.split(' '), visible };
            apiMethod = 'POST';
        }
        else {
            if (listid == null || listid == '') {
                // ok now we panic
                d('No listid or item id provided!');
                return;
            } else {
                // if id is not null or empty, then this is an existing item
                // and we need to call the API to update the list
                apiUrl = apiUrl + '/modifyitem';
                data = { id, listid, name, comment, tags: tags.split(' '), visible };
                apiMethod = 'PUT';
            }
        }
        
        let displayResults = "";
        let newID = null;
        // Call the REST API
        console.info(' | Calling API: ' + apiUrl + ' with data: ' + JSON.stringify(data));
        fetch(apiUrl, {
            method: apiMethod,
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(data),
        })
        .then(response => {
            if (response.ok) {
                return response;
            }
            else if (response.status == RC.NOT_FOUND) {
                throw new Error(`Item ${id} in list ${listid} not found!`);
            }
            else if (response.status == RC.UNAUTHORIZED) {
                throw new Error('Not authorized - have you logged in yet? <a href="_login.html">Login</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                throw new Error('Forbidden - have you logged in yet? <a href="_login.html">Login</a>');
            }
            else {
                throw new Error('Error ' + response.status + ' - ' + response.statusText);
            }
        })
            .then(response => {
                console.log('Response IS:', response);
                let contentType = response.headers.get("Content-Type");
                if (contentType == null) contentType = 'text/plain';
                console.log('Content-Type IS:', contentType);
                if (contentType.includes("application/json")) {
                    return response.json().then(json => {
                        newID = json.id;
                        console.log('NEW ID IS:', newID);
                        displayResults = "ITEM Created: " + JSON.stringify(json);
                        console.log('displayResults IS:', displayResults);
                        d(displayResults);

                        // now that we have newID, populdate the id field in the form
                        // but only if we're in "update" mode
                        // get the value of the radio button neworupdate
                        // if its new then leave the id field alone
                        let newOrUpdate = document.querySelector('input[name="neworupdate"]:checked').value;
                        console.debug(' | newOrUpdate: ' + newOrUpdate);
                        if (newOrUpdate == 'update') {
                            document.getElementById('item.id').value = newID;
                            // and update the url in the browser to include the newID
                            let newUrl = window.location.href;
                            newUrl = newUrl.substring(0, newUrl.indexOf('?'));
                            newUrl = newUrl + '?item=' + newID;
                            window.history.pushState({}, '', newUrl);
                            console.debug(' | New URL: ' + newUrl);
                        }
                    });
                } else if (contentType.includes("text") || contentType == null) {
                    return response.text().then(text => {
                        displayResults = "Item modified! ";
                        console.log('displayResults IS:', displayResults);
                        d(displayResults);
                        c(RC.OK);
                    });
                } else {
                    return response.text().then(text => {
                        displayResults = "NO IDEA: " + text;
                        d(displayResults);
                        c(RC.ERROR);
                    });
                }


            });

    }
    catch (error) {
        d(error);
        c(RC.ERROR);
        console.error(error);
    }

}