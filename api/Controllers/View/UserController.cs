using api.Dal.Interface;
using api.Models;
using api.Security;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;

namespace api.Controllers.View
{


    public class UserController : Controller
    {

        private static string roleName = "";
        // Initialize HTTP CLIENT

        private readonly IApi _service;
        public UserController(IApi service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        // Pregenerated List
        private IEnumerable<UserGroup> UserGroups() // Stavljeno je static da se ucita samo jednom, jer aplikacija ne omogucuje dodavanje rola, nije potrebno. Ali ostavljamo shemu za buducnost.
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);

                IEnumerable<UserGroup> userGroups = RepoFactory.GetApi().UserGroupsGet(jwtKey).Result;

                return userGroups;
            }
            catch (Exception e)
            {
                throw;
            }
        }



        // GET: UserViewController1
        public ActionResult Index()
        {
            try
            {
                // check if user logged in
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                IEnumerable<User> users = RepoFactory.GetApi().UsersGet(jwtKey).Result; // Mora biti result na kraju za Task

                return View(users);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }

        }


        public ActionResult Details(int? idUser)
        {
            try
            {

                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                User user = RepoFactory.GetApi().UserGet(jwtKey, idUser, null, null).Result;

                return View(user);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }


        public ActionResult Create()
        {

            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                UserView userView = new UserView();
                userView.UserGroups = UserGroups();
                return View(userView);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(UserView userView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }


                UserAdd userAdd = new UserAdd();

                userAdd.TenantID = userView.UserAdd.TenantID;
                userAdd.Email = userView.UserAdd.Email;
                userAdd.Password = userView.UserAdd.Password;
                userAdd.Username = userView.UserAdd.Username;
                userAdd.FirstName = userView.UserAdd.FirstName;
                userAdd.LastName = userView.UserAdd.LastName;
                userAdd.Phone = userView.UserAdd.Phone;
                userAdd.UserGroupID = userView.UserAdd.UserGroupID;
                userAdd.Enabled = userView.UserAdd.Enabled;


                RepoFactory.GetApi().UserAdd(jwtKey, userAdd);

                return RedirectToAction("Index");

            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        // GET: UserViewController1/Edit/5
        public ActionResult Edit(int? idUser)
        {

            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }


                UserView? userView = new UserView();
                User value = RepoFactory.GetApi().UserGet(jwtKey, idUser, null, null).Result;

                // User populate
                UserUpdate userUpdate = new UserUpdate();
                userUpdate.IDUser = value.IDUser;
                userUpdate.TenantID = value.TenantID;
                userUpdate.Email = value.Email;
                userUpdate.DevicePin = value.DevicePin;
                userUpdate.Username = value.Username;
                userUpdate.FirstName = value.FirstName;
                userUpdate.LastName = value.LastName;
                userUpdate.Phone = value.Phone;
                userUpdate.UserGroupID = value.UserGroupID;
                userUpdate.Enabled = value.Enabled ?? false;

                // UPDATE model
                userView.UserUpdate = userUpdate;


                // Get Roles and update model
                userView.UserGroups = UserGroups();

                return View(userView);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(UserView userView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                UserUpdate userUpdate = userView.UserUpdate;

                if (RepoFactory.GetApi().UserUpdate(jwtKey, userUpdate).Result)
                {
                    bool result = RepoFactory.GetApi().UserUpdate(jwtKey, userUpdate).Result;
                }


                User user = RepoFactory.GetApi().UserGet(jwtKey, userView.UserUpdate.IDUser, null, null).Result;
                return View("Details", user);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }


        public ActionResult Delete(int? idUser)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                User user = RepoFactory.GetApi().UserGet(jwtKey, idUser, null, null).Result;
                return View(user);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirm(int? idUser)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                RepoFactory.GetApi().UserDelete(jwtKey, idUser);

                return RedirectToAction(nameof(Index));
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }



    }
}
