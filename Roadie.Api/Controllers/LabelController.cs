﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Roadie.Api.Services;
using Roadie.Library.Caching;
using Roadie.Library.Identity;
using Roadie.Library.Models.Pagination;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using models = Roadie.Library.Models;

namespace Roadie.Api.Controllers
{
    [Produces("application/json")]
    [Route("labels")]
    [ApiController]
    [Authorize]
    public class LabelController : EntityControllerBase
    {
        private ILabelService LabelService { get; }

        public LabelController(ILabelService labelService, ILoggerFactory logger, ICacheManager cacheManager, IConfiguration configuration, UserManager<ApplicationUser> userManager)
            : base(cacheManager, configuration, userManager)
        {
            this.Logger = logger.CreateLogger("RoadieApi.Controllers.LabelController");
            this.LabelService = labelService;
        }

        [HttpGet("{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Get(Guid id, string inc = null)
        {
            var result = await this.LabelService.ById(await this.CurrentUserModel(), id, (inc ?? models.Label.DefaultIncludes).ToLower().Split(","));
            if (result == null || result.IsNotFoundResult)
            {
                return NotFound();
            }
            if (!result.IsSuccess)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
            return Ok(result);
        }

        [HttpGet]
        [ProducesResponseType(200)]
        public async Task<IActionResult> List([FromQuery]PagedRequest request, bool? doRandomize = false)
        {
            try
            {
                var result = await this.LabelService.List(roadieUser: await this.CurrentUserModel(),
                                                    request: request,
                                                    doRandomize: doRandomize);
                if (!result.IsSuccess)
                {
                    return StatusCode((int)HttpStatusCode.InternalServerError);
                }
                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode((int)HttpStatusCode.Unauthorized);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex);
            }
            return StatusCode((int)HttpStatusCode.InternalServerError);
        }

        [HttpPost("uploadImage/{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [Authorize(Policy = "Editor")]
        public async Task<IActionResult> UploadImage(Guid id, IFormFile file)
        {
            var result = await this.LabelService.UploadLabelImage(await this.CurrentUserModel(), id, file);
            if (result == null || result.IsNotFoundResult)
            {
                return NotFound();
            }
            if (!result.IsSuccess)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
            return Ok(result);
        }

        [HttpPost("setImageByUrl/{id}/{imageUrl}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [Authorize(Policy = "Editor")]
        public async Task<IActionResult> SetLabelImageByUrl(Guid id, string imageUrl)
        {
            var result = await this.LabelService.SetLabelImageByUrl(await this.CurrentUserModel(), id, HttpUtility.UrlDecode(imageUrl));
            if (result == null || result.IsNotFoundResult)
            {
                return NotFound();
            }
            if (!result.IsSuccess)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
            return Ok(result);
        }

        [HttpPost("edit")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [Authorize(Policy = "Editor")]
        public async Task<IActionResult> Update(models.Label label)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            var result = await this.LabelService.UpdateLabel(await this.CurrentUserModel(), label);
            if (result == null || result.IsNotFoundResult)
            {
                return NotFound();
            }
            if (!result.IsSuccess)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
            return Ok(result);
        }
    }
}