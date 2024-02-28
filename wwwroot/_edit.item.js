
document.addEventListener('DOMContentLoaded', getItem);



// function that is called when _edit.item.html is called that
// gets the listid from the url querystring and then 
// populates the form in _edit.item.html with the list data
function getItem() {
    console.log('getItem')

    if (islocal()) return;

    // Get the itemid from the querystring
    let urlParams = new URLSearchParams(window.location.search);
    let itemid = urlParams.get('itemid');
    console.debug(' | itemid: ' + itemid);
    let listid = urlParams.get('listid');
    console.debug(' | listid: ' + listid);
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
            return;
        } else {


            // populate the listid field in the form
            document.getElementById('item.listid').value = listid;
            d('Creating new item in list ' + listid + '.');

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
            return;
        } else {
            // populate the listid field in the form
            document.getElementById('item.listid').value = listid;
            // populate the itemid field in the form
            document.getElementById('item.id').value = itemid;
            d('Editing item ' + itemid + ' in list ' + listid + '.');
            // oh and set the radio button to "update"
            document.getElementById('update').checked = true;
        }
    }


    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.item.html'));
    console.debug(' | API URL: ' + apiUrl);
    if (itemid != null) {
        console.debug(' | API URL: ' + apiUrl);
        console.debug(' | Getting item ' + itemid + ' in list ' + listid + '.')
        fetch(apiUrl + '/showitems/' + listid + '/' + itemid)
            .then(response => response.json())
            .then(data => {
                console.log('Success:', data);
                // Populate the form with the data from the API
                document.getElementById('item.id').value = data.id;
                document.getElementById('item.name').value = data.name;
                //document.getElementById('item.comment').value = data.comment;
                simplemde.value(data.comment);
                document.getElementById('item.tags').value = data.tags.join(' ');
            })
            .catch((error) => {
                // write any error to the span with id="result"
                d(error);

                console.error('Error:', error);
            });
    }
    // Get the list NAME for the "back to list page" link
    fetch(apiUrl + '/showlists/' + listid)
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

            console.error('Error:', error);
        });


}

// When the form is submitted, send it to the REST API
document.getElementById('edititemform').addEventListener('submit', updateItem);

async function updateItem(e) {
    e.preventDefault();
    if (islocal()) return;
    console.log('updateItem');
    let apiUrl = "";

    try {
        apiUrl = window.location.href;
        // get just the hostname and port from the url
        apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.item.html'));
        myUrl = apiUrl;
        console.debug(' | API URL: ' + apiUrl);

        let id = document.getElementById('item.id').value;
        let listid = document.getElementById('item.listid').value;
        let name = document.getElementById('item.name').value;
        //let comment = document.getElementById('item.comment').value;
        let comment = simplemde.value();
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
            apiUrl = apiUrl + '/additem/' + listid;
            data = { listid, name, comment, tags: tags.split(' ') };
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
                data = { id, listid, name, comment, tags: tags.split(' ') };
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

                    });
                } else {
                    return response.text().then(text => {
                        displayResults = "NO IDEA: " + text;
                        d(displayResults);
                    });
                }


            });

    }
    catch (error) {
        d(error);
        console.error('Error:', error);
    }

}